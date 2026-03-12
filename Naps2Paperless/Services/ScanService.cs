using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Naps2Paperless.Models;

namespace Naps2Paperless.Services;

public class ScanService
{
    private readonly HttpClient _http = new();

    public async Task ScanAsync(
        AppSettings settings,
        string source,
        string splitMode,
        Action<string> log,
        CancellationToken ct)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var tempBase = Path.Combine(Path.GetTempPath(), $"scan_{timestamp}");
        var scanFiles = new List<string>();

        try
        {
            var effectiveSplitMode = source == "glass" ? "single" : splitMode;

            if (source == "manualduplex")
            {
                scanFiles.AddRange(await ManualDuplexScanAsync(settings, effectiveSplitMode, tempBase, log, ct));
            }
            else
            {
                scanFiles.AddRange(await StandardScanAsync(settings, source, effectiveSplitMode, tempBase, log, ct));
            }

            if (scanFiles.Count == 0)
            {
                log("Scan fehlgeschlagen - keine Ausgabedateien.");
                return;
            }

            foreach (var file in scanFiles)
            {
                ct.ThrowIfCancellationRequested();
                await UploadAsync(settings, file, log, ct);
                File.Delete(file);
                log($"Hochgeladen: {Path.GetFileName(file)}");
            }

            log("Alle Uploads abgeschlossen!");
        }
        finally
        {
            foreach (var f in scanFiles.Where(File.Exists))
                File.Delete(f);
        }
    }

    private async Task<List<string>> ManualDuplexScanAsync(
        AppSettings settings, string splitMode, string tempBase, Action<string> log, CancellationToken ct)
    {
        if (splitMode == "perpage")
            return await ManualDuplexPerPageAsync(settings, tempBase, log, ct);

        var frontPdf = $"{tempBase}_front.pdf";
        var backPdf = $"{tempBase}_back.pdf";
        var mergedPdf = $"{tempBase}_merged.pdf";

        // Pass 1: front sides
        log("Vorderseiten werden gescannt...");
        await RunNaps2Async(settings, ["-o", frontPdf, "-p", settings.ProfileName, "--source", "feeder"], log, ct);

        if (!File.Exists(frontPdf))
        {
            log("Scan fehlgeschlagen - keine Vorderseiten gefunden.");
            return [];
        }
        log("Vorderseiten gescannt.");

        // Prompt user to flip
        var tcs = new TaskCompletionSource<bool>();
        FlipStackRequested?.Invoke(tcs);
        await tcs.Task;

        // Pass 2: back sides
        log("Rueckseiten werden gescannt...");
        await RunNaps2Async(settings,
            ["-o", backPdf, "-p", settings.ProfileName, "--source", "feeder", "--reverse", "--waitscan", "--firstnow"],
            log, ct);

        if (!File.Exists(backPdf))
        {
            log("Scan fehlgeschlagen - keine Rueckseiten gefunden.");
            if (File.Exists(frontPdf)) File.Delete(frontPdf);
            return [];
        }
        log("Rueckseiten gescannt.");

        // Merge
        await RunNaps2Async(settings,
            ["-o", mergedPdf, "-i", $"{frontPdf};{backPdf}", "--altinterleave"],
            log, ct);

        if (File.Exists(mergedPdf))
        {
            File.Delete(frontPdf);
            File.Delete(backPdf);
            log("Seiten zusammengefuegt.");
            return [mergedPdf];
        }

        log("Zusammenfuehren fehlgeschlagen, lade Einzel-PDFs hoch...");
        var result = new List<string>();
        if (File.Exists(frontPdf)) result.Add(frontPdf);
        if (File.Exists(backPdf)) result.Add(backPdf);
        return result;
    }

    private async Task<List<string>> ManualDuplexPerPageAsync(
        AppSettings settings, string tempBase, Action<string> log, CancellationToken ct)
    {
        var frontBase = $"{tempBase}_front";
        var backBase = $"{tempBase}_back";

        // Pass 1: front sides, split per page
        log("Vorderseiten werden gescannt (Per Page)...");
        await RunNaps2Async(settings,
            ["-o", $"{frontBase}.$(n).pdf", "-p", settings.ProfileName, "--source", "feeder", "--split"],
            log, ct);

        var frontFiles = Directory.GetFiles(Path.GetTempPath(), "*.pdf")
            .Where(f => f.StartsWith(frontBase))
            .OrderBy(f => f)
            .ToList();

        if (frontFiles.Count == 0)
        {
            log("Scan fehlgeschlagen - keine Vorderseiten gefunden.");
            return [];
        }
        log($"{frontFiles.Count} Vorderseite(n) gescannt.");

        // Prompt user to flip
        var tcs = new TaskCompletionSource<bool>();
        FlipStackRequested?.Invoke(tcs);
        await tcs.Task;

        // Pass 2: back sides, split per page
        log("Rueckseiten werden gescannt (Per Page)...");
        await RunNaps2Async(settings,
            ["-o", $"{backBase}.$(n).pdf", "-p", settings.ProfileName, "--source", "feeder", "--split"],
            log, ct);

        var backFiles = Directory.GetFiles(Path.GetTempPath(), "*.pdf")
            .Where(f => f.StartsWith(backBase))
            .OrderBy(f => f)
            .ToList();

        if (backFiles.Count == 0)
        {
            log("Scan fehlgeschlagen - keine Rueckseiten gefunden.");
            foreach (var f in frontFiles.Where(File.Exists)) File.Delete(f);
            return [];
        }
        log($"{backFiles.Count} Rueckseite(n) gescannt.");

        // Pair front page i with back page (n - i + 1):
        // Front: 1, 2, 3, ... n  (in scan order)
        // Back:  n, n-1, ..., 1  (scanner outputs in reverse physical order, but we scanned reversed stack)
        // So back file 1 = back of front n, back file 2 = back of front n-1, etc.
        // We need: doc1 = front1 + back_n, doc2 = front2 + back_(n-1), ...
        var n = frontFiles.Count;
        var mergedFiles = new List<string>();

        for (var i = 0; i < n; i++)
        {
            var backIndex = n - 1 - i;
            if (backIndex >= backFiles.Count)
            {
                log($"Warnung: Keine Rueckseite fuer Vorderseite {i + 1}, ueberspringe.");
                continue;
            }

            var mergedPdf = $"{tempBase}_doc{i + 1}.pdf";
            await RunNaps2Async(settings,
                ["-o", mergedPdf, "-i", $"{frontFiles[i]};{backFiles[backIndex]}"],
                log, ct);

            if (File.Exists(mergedPdf))
            {
                mergedFiles.Add(mergedPdf);
                log($"Dokument {i + 1}: Vorderseite {i + 1} + Rueckseite {n - i} zusammengefuegt.");
            }
            else
            {
                log($"Warnung: Zusammenfuehren von Dokument {i + 1} fehlgeschlagen.");
                mergedFiles.Add(frontFiles[i]);
                frontFiles[i] = ""; // prevent cleanup
            }
        }

        // Clean up individual page files
        foreach (var f in frontFiles.Concat(backFiles).Where(f => f != "" && File.Exists(f)))
            File.Delete(f);

        log($"{mergedFiles.Count} Dokument(e) erstellt.");
        return mergedFiles;
    }

    private async Task<List<string>> StandardScanAsync(
        AppSettings settings, string source, string splitMode, string tempBase,
        Action<string> log, CancellationToken ct)
    {
        var useSplit = splitMode is "perpage" or "patcht";
        var outputPath = useSplit ? $"{tempBase}.$(n).pdf" : $"{tempBase}.pdf";
        var args = new List<string> { "-o", outputPath, "-p", settings.ProfileName, "--source", source };

        if (splitMode == "perpage") args.Add("--split");
        else if (splitMode == "patcht") args.Add("--splitpatcht");

        log($"Scanvorgang gestartet ({source})...");
        await RunNaps2Async(settings, args, log, ct);

        var files = Directory.GetFiles(Path.GetTempPath(), "*.pdf")
            .Where(f => f.StartsWith(tempBase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count > 0)
            log($"{files.Count} Datei(en) gescannt.");

        return files;
    }

    private async Task RunNaps2Async(AppSettings settings, List<string> args, Action<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = settings.Naps2Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
        {
            log("NAPS2 konnte nicht gestartet werden.");
            return;
        }

        var outputTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
                log(line);
        }, ct);

        var errorTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(ct) is { } line)
                log($"[ERR] {line}");
        }, ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(outputTask, errorTask);
    }

    private async Task UploadAsync(AppSettings settings, string filePath, Action<string> log, CancellationToken ct)
    {
        log($"Lade hoch: {Path.GetFileName(filePath)}...");

        var baseUrl = settings.ApiBaseUrl.TrimEnd('/');
        var endpoint = settings.ApiEndpoint.TrimStart('/');
        var fullUrl = $"{baseUrl}/{endpoint}";

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "document", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
        request.Headers.Add("Authorization", $"Token {settings.ApiToken}");
        request.Content = form;

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            log($"Upload-Fehler: {response.StatusCode}");
    }

    public event Action<TaskCompletionSource<bool>>? FlipStackRequested;
}
