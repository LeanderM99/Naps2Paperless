# Naps2Paperless WPF App Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the Scan.ps1 PowerShell script into a WPF application with a main scan view and a settings view.

**Architecture:** Two-view MVVM app. `SettingsService` handles JSON persistence. `ScanService` wraps NAPS2 process execution and Paperless HTTP upload. ViewModels expose commands and properties; views bind to them. Navigation swaps content in MainWindow.

**Tech Stack:** .NET 10, WPF, System.Text.Json, System.Net.Http, System.Diagnostics.Process

---

## File Structure

```
Naps2Paperless/
├── App.xaml                      (modify - add resources/styles)
├── App.xaml.cs                   (no change)
├── MainWindow.xaml               (modify - navigation host)
├── MainWindow.xaml.cs            (modify - navigation logic)
├── Models/
│   └── AppSettings.cs            (create - settings POCO)
├── Services/
│   ├── SettingsService.cs        (create - JSON load/save)
│   └── ScanService.cs            (create - NAPS2 + upload logic)
├── ViewModels/
│   ├── RelayCommand.cs           (create - ICommand impl)
│   ├── BaseViewModel.cs          (create - INotifyPropertyChanged)
│   ├── ScanViewModel.cs          (create - scan view logic)
│   └── SettingsViewModel.cs      (create - settings view logic)
├── Views/
│   ├── ScanView.xaml             (create - scan UI)
│   ├── ScanView.xaml.cs          (create - codebehind)
│   ├── SettingsView.xaml         (create - settings UI)
│   └── SettingsView.xaml.cs      (create - codebehind)
└── Naps2Paperless.csproj         (no change)
```

---

## Chunk 1: Foundation — Models, Services, MVVM Base

### Task 1: AppSettings Model

**Files:**
- Create: `Naps2Paperless/Models/AppSettings.cs`

- [ ] **Step 1: Create the AppSettings class**

```csharp
namespace Naps2Paperless.Models;

public class AppSettings
{
    public string Naps2Path { get; set; } = @"C:\Program Files\NAPS2\NAPS2.Console.exe";
    public string ProfileName { get; set; } = "PaperlessScan";
    public string ApiUrl { get; set; } = "https://paperless.lmihm.de/api/documents/post_document/";
    public string ApiToken { get; set; } = "";
}
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/Models/AppSettings.cs
git commit -m "feat: add AppSettings model"
```

---

### Task 2: SettingsService

**Files:**
- Create: `Naps2Paperless/Services/SettingsService.cs`

- [ ] **Step 1: Create SettingsService**

Loads/saves `AppSettings` as JSON to `settings.json` next to the executable. Creates defaults if file missing.

```csharp
using System.IO;
using System.Text.Json;
using Naps2Paperless.Models;

namespace Naps2Paperless.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/Services/SettingsService.cs
git commit -m "feat: add SettingsService for JSON persistence"
```

---

### Task 3: MVVM Base Classes

**Files:**
- Create: `Naps2Paperless/ViewModels/BaseViewModel.cs`
- Create: `Naps2Paperless/ViewModels/RelayCommand.cs`

- [ ] **Step 1: Create BaseViewModel**

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Naps2Paperless.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
```

- [ ] **Step 2: Create RelayCommand**

```csharp
using System.Windows.Input;

namespace Naps2Paperless.ViewModels;

public class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}
```

- [ ] **Step 3: Commit**

```bash
git add Naps2Paperless/ViewModels/BaseViewModel.cs Naps2Paperless/ViewModels/RelayCommand.cs
git commit -m "feat: add MVVM base classes"
```

---

### Task 4: ScanService

**Files:**
- Create: `Naps2Paperless/Services/ScanService.cs`

- [ ] **Step 1: Create ScanService**

This is the core logic ported from Scan.ps1. It runs NAPS2 as a process and uploads via HttpClient.

```csharp
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
            if (source == "manualduplex")
            {
                scanFiles.AddRange(await ManualDuplexScanAsync(settings, tempBase, log, ct));
            }
            else
            {
                scanFiles.AddRange(await StandardScanAsync(settings, source, splitMode, tempBase, log, ct));
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
            // Clean up any remaining temp files
            foreach (var f in scanFiles.Where(File.Exists))
                File.Delete(f);
        }
    }

    private async Task<List<string>> ManualDuplexScanAsync(
        AppSettings settings, string tempBase, Action<string> log, CancellationToken ct)
    {
        var frontPdf = $"{tempBase}_front.pdf";
        var backPdf = $"{tempBase}_back.pdf";
        var mergedPdf = $"{tempBase}_merged.pdf";

        // Pass 1: front sides
        log("Vorderseiten werden gescannt...");
        await RunNaps2Async(settings, [$"-o", frontPdf, "-p", settings.ProfileName, "--source", "feeder"], log, ct);

        if (!File.Exists(frontPdf))
        {
            log("Scan fehlgeschlagen - keine Vorderseiten gefunden.");
            return [];
        }
        log("Vorderseiten gescannt.");

        // Prompt user to flip — handled via callback that shows a dialog
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

    private async Task<List<string>> StandardScanAsync(
        AppSettings settings, string source, string splitMode, string tempBase,
        Action<string> log, CancellationToken ct)
    {
        var args = new List<string> { "-o", $"{tempBase}.{{num}}.pdf", "-p", settings.ProfileName, "--source", source };

        if (splitMode == "perpage") args.Add("--split");
        else if (splitMode == "patcht") args.Add("--splitpatcht");

        log($"Scanvorgang gestartet ({source})...");
        await RunNaps2Async(settings, args, log, ct);

        var pattern = $"scan_{Path.GetFileName(tempBase).Replace("scan_", "")}*.pdf";
        var files = Directory.GetFiles(Path.GetTempPath(), pattern)
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

        // Read output async
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

        using var form = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "document", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.ApiUrl);
        request.Headers.Add("Authorization", $"Token {settings.ApiToken}");
        request.Content = form;

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            log($"Upload-Fehler: {response.StatusCode}");
    }

    /// <summary>
    /// Event raised during manual duplex when the user needs to flip the stack.
    /// The handler must complete the TaskCompletionSource when the user is ready.
    /// </summary>
    public event Action<TaskCompletionSource<bool>>? FlipStackRequested;
}
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/Services/ScanService.cs
git commit -m "feat: add ScanService with NAPS2 process and upload logic"
```

---

## Chunk 2: ViewModels

### Task 5: ScanViewModel

**Files:**
- Create: `Naps2Paperless/ViewModels/ScanViewModel.cs`

- [ ] **Step 1: Create ScanViewModel**

```csharp
using System.Windows;
using System.Windows.Input;
using Naps2Paperless.Services;

namespace Naps2Paperless.ViewModels;

public class ScanViewModel : BaseViewModel
{
    private readonly ScanService _scanService;
    private readonly SettingsService _settingsService;
    private string _logText = "";
    private bool _isScanning;
    private string _selectedSplitMode = "perpage";
    private CancellationTokenSource? _cts;

    public ScanViewModel(ScanService scanService, SettingsService settingsService)
    {
        _scanService = scanService;
        _settingsService = settingsService;

        ScanGlassCommand = new RelayCommand(_ => StartScan("glass"), _ => !IsScanning);
        ScanFeederCommand = new RelayCommand(_ => StartScan("feeder"), _ => !IsScanning);
        ScanDuplexCommand = new RelayCommand(_ => StartScan("duplex"), _ => !IsScanning);
        ScanManualDuplexCommand = new RelayCommand(_ => StartScan("manualduplex"), _ => !IsScanning);
        OpenSettingsCommand = new RelayCommand(_ => NavigateToSettingsRequested?.Invoke());
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsScanning);

        _scanService.FlipStackRequested += OnFlipStackRequested;
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public string SelectedSplitMode
    {
        get => _selectedSplitMode;
        set => SetProperty(ref _selectedSplitMode, value);
    }

    public List<string> SplitModes { get; } = ["single", "perpage", "patcht"];
    public List<string> SplitModeLabels { get; } = ["Single", "Per Page", "Patch-T"];

    public ICommand ScanGlassCommand { get; }
    public ICommand ScanFeederCommand { get; }
    public ICommand ScanDuplexCommand { get; }
    public ICommand ScanManualDuplexCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action? NavigateToSettingsRequested;

    private async void StartScan(string source)
    {
        var settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(settings.ApiToken))
        {
            Log("Fehler: API-Token ist nicht konfiguriert. Bitte Einstellungen pruefen.");
            return;
        }

        IsScanning = true;
        LogText = "";
        _cts = new CancellationTokenSource();

        try
        {
            await _scanService.ScanAsync(settings, source, SelectedSplitMode, Log, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Scan abgebrochen.");
        }
        catch (Exception ex)
        {
            Log($"Fehler: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Log(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogText += message + Environment.NewLine;
        });
    }

    private void OnFlipStackRequested(TaskCompletionSource<bool> tcs)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var result = MessageBox.Show(
                "Bitte den gesamten Stapel umdrehen und wieder in den Einzug legen.\n\n(Letzte Seite liegt jetzt oben)\n\nBereit?",
                "Stapel umdrehen",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.OK)
                tcs.SetResult(true);
            else
                tcs.SetCanceled();
        });
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/ViewModels/ScanViewModel.cs
git commit -m "feat: add ScanViewModel with scan commands and logging"
```

---

### Task 6: SettingsViewModel

**Files:**
- Create: `Naps2Paperless/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Create SettingsViewModel**

```csharp
using System.Windows.Input;
using Naps2Paperless.Services;

namespace Naps2Paperless.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;
    private string _naps2Path = "";
    private string _profileName = "";
    private string _apiUrl = "";
    private string _apiToken = "";

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        SaveCommand = new RelayCommand(_ => Save());
        BrowseNaps2Command = new RelayCommand(_ => BrowseNaps2());
        Load();
    }

    public string Naps2Path
    {
        get => _naps2Path;
        set => SetProperty(ref _naps2Path, value);
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public string ApiUrl
    {
        get => _apiUrl;
        set => SetProperty(ref _apiUrl, value);
    }

    public string ApiToken
    {
        get => _apiToken;
        set => SetProperty(ref _apiToken, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseNaps2Command { get; }

    public event Action? NavigateBackRequested;

    private void Load()
    {
        var s = _settingsService.Load();
        Naps2Path = s.Naps2Path;
        ProfileName = s.ProfileName;
        ApiUrl = s.ApiUrl;
        ApiToken = s.ApiToken;
    }

    private void Save()
    {
        _settingsService.Save(new Models.AppSettings
        {
            Naps2Path = Naps2Path,
            ProfileName = ProfileName,
            ApiUrl = ApiUrl,
            ApiToken = ApiToken
        });
        NavigateBackRequested?.Invoke();
    }

    private void BrowseNaps2()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable|*.exe",
            Title = "NAPS2.Console.exe auswaehlen"
        };
        if (dialog.ShowDialog() == true)
            Naps2Path = dialog.FileName;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/ViewModels/SettingsViewModel.cs
git commit -m "feat: add SettingsViewModel with save/load and browse"
```

---

## Chunk 3: Views and MainWindow

### Task 7: ScanView

**Files:**
- Create: `Naps2Paperless/Views/ScanView.xaml`
- Create: `Naps2Paperless/Views/ScanView.xaml.cs`

- [ ] **Step 1: Create ScanView XAML**

```xml
<UserControl x:Class="Naps2Paperless.Views.ScanView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Top bar: Settings button -->
        <DockPanel Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="Naps2 Paperless" FontSize="20" FontWeight="Bold"
                       VerticalAlignment="Center"/>
            <Button Content="Einstellungen" Command="{Binding OpenSettingsCommand}"
                    HorizontalAlignment="Right" Padding="12,6" />
        </DockPanel>

        <!-- Split mode -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Split-Modus:" VerticalAlignment="Center" Margin="0,0,8,0"/>
            <ComboBox Width="140"
                      ItemsSource="{Binding SplitModeLabels}"
                      SelectedIndex="{Binding SelectedSplitModeIndex, Mode=TwoWay}"/>
        </StackPanel>

        <!-- Scan buttons -->
        <UniformGrid Grid.Row="2" Columns="5" Margin="0,0,0,12">
            <Button Content="Glass" Command="{Binding ScanGlassCommand}"
                    Margin="0,0,6,0" Padding="8,12" FontSize="14"/>
            <Button Content="Feeder" Command="{Binding ScanFeederCommand}"
                    Margin="3,0,6,0" Padding="8,12" FontSize="14"/>
            <Button Content="Duplex" Command="{Binding ScanDuplexCommand}"
                    Margin="3,0,6,0" Padding="8,12" FontSize="14"/>
            <Button Content="Manual Duplex" Command="{Binding ScanManualDuplexCommand}"
                    Margin="3,0,6,0" Padding="8,12" FontSize="14"/>
            <Button Content="Abbrechen" Command="{Binding CancelCommand}"
                    Margin="3,0,0,0" Padding="8,12" FontSize="14"
                    Foreground="Red"/>
        </UniformGrid>

        <!-- Log output -->
        <TextBox Grid.Row="3" Text="{Binding LogText, Mode=OneWay}"
                 IsReadOnly="True" VerticalScrollBarVisibility="Auto"
                 TextWrapping="Wrap" FontFamily="Consolas" FontSize="12"
                 Background="#1E1E1E" Foreground="#DCDCDC"
                 Padding="8" BorderThickness="1" BorderBrush="#333"
                 x:Name="LogBox"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create ScanView codebehind**

```csharp
using System.Windows.Controls;

namespace Naps2Paperless.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
        // Auto-scroll log to bottom
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.ScanViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(vm.LogText))
                        LogBox.ScrollToEnd();
                };
            }
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Naps2Paperless/Views/ScanView.xaml Naps2Paperless/Views/ScanView.xaml.cs
git commit -m "feat: add ScanView with buttons, split mode, and log panel"
```

---

### Task 8: SettingsView

**Files:**
- Create: `Naps2Paperless/Views/SettingsView.xaml`
- Create: `Naps2Paperless/Views/SettingsView.xaml.cs`

- [ ] **Step 1: Create SettingsView XAML**

```xml
<UserControl x:Class="Naps2Paperless.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Einstellungen" FontSize="20" FontWeight="Bold"
                   Margin="0,0,0,16"/>

        <StackPanel Grid.Row="1" MaxWidth="500" HorizontalAlignment="Left">
            <!-- NAPS2 Path -->
            <TextBlock Text="NAPS2 Pfad:" Margin="0,0,0,4"/>
            <DockPanel Margin="0,0,0,12">
                <Button Content="..." DockPanel.Dock="Right" Padding="8,4"
                        Command="{Binding BrowseNaps2Command}" Margin="6,0,0,0"/>
                <TextBox Text="{Binding Naps2Path, UpdateSourceTrigger=PropertyChanged}"/>
            </DockPanel>

            <!-- Profile Name -->
            <TextBlock Text="Profilname:" Margin="0,0,0,4"/>
            <TextBox Text="{Binding ProfileName, UpdateSourceTrigger=PropertyChanged}"
                     Margin="0,0,0,12"/>

            <!-- API URL -->
            <TextBlock Text="API URL:" Margin="0,0,0,4"/>
            <TextBox Text="{Binding ApiUrl, UpdateSourceTrigger=PropertyChanged}"
                     Margin="0,0,0,12"/>

            <!-- API Token -->
            <TextBlock Text="API Token:" Margin="0,0,0,4"/>
            <PasswordBox x:Name="ApiTokenBox" Margin="0,0,0,12"/>
        </StackPanel>

        <Button Grid.Row="2" Content="Speichern &amp; Zurueck" Command="{Binding SaveCommand}"
                HorizontalAlignment="Left" Padding="16,8" FontSize="14"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create SettingsView codebehind**

PasswordBox doesn't support binding, so we wire it manually:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace Naps2Paperless.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                ApiTokenBox.Password = vm.ApiToken;
                ApiTokenBox.PasswordChanged += (_, _) => vm.ApiToken = ApiTokenBox.Password;
            }
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Naps2Paperless/Views/SettingsView.xaml Naps2Paperless/Views/SettingsView.xaml.cs
git commit -m "feat: add SettingsView with form fields and browse"
```

---

### Task 9: Update ScanViewModel for ComboBox Index Binding

The ComboBox binds to `SelectedSplitModeIndex` rather than the string value directly, so add that property.

**Files:**
- Modify: `Naps2Paperless/ViewModels/ScanViewModel.cs`

- [ ] **Step 1: Replace SelectedSplitMode with index-based binding**

Remove the `SelectedSplitMode` string property. Add:

```csharp
private int _selectedSplitModeIndex = 1; // default "perpage"

public int SelectedSplitModeIndex
{
    get => _selectedSplitModeIndex;
    set => SetProperty(ref _selectedSplitModeIndex, value);
}

// Used internally when starting scan
public string SelectedSplitMode => SplitModes[SelectedSplitModeIndex];
```

- [ ] **Step 2: Commit**

```bash
git add Naps2Paperless/ViewModels/ScanViewModel.cs
git commit -m "fix: use index-based ComboBox binding for split mode"
```

---

### Task 10: MainWindow — Navigation Host

**Files:**
- Modify: `Naps2Paperless/MainWindow.xaml`
- Modify: `Naps2Paperless/MainWindow.xaml.cs`

- [ ] **Step 1: Update MainWindow.xaml**

```xml
<Window x:Class="Naps2Paperless.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Naps2 Paperless" Height="500" Width="650"
        WindowStartupLocation="CenterScreen">
    <ContentControl x:Name="ContentArea"/>
</Window>
```

- [ ] **Step 2: Update MainWindow.xaml.cs**

```csharp
using System.Windows;
using Naps2Paperless.Services;
using Naps2Paperless.ViewModels;
using Naps2Paperless.Views;

namespace Naps2Paperless;

public partial class MainWindow : Window
{
    private readonly ScanView _scanView;
    private readonly SettingsView _settingsView;

    public MainWindow()
    {
        InitializeComponent();

        var settingsService = new SettingsService();
        var scanService = new ScanService();

        var scanVm = new ScanViewModel(scanService, settingsService);
        var settingsVm = new SettingsViewModel(settingsService);

        _scanView = new ScanView { DataContext = scanVm };
        _settingsView = new SettingsView { DataContext = settingsVm };

        scanVm.NavigateToSettingsRequested += () => ContentArea.Content = _settingsView;
        settingsVm.NavigateBackRequested += () => ContentArea.Content = _scanView;

        ContentArea.Content = _scanView;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Naps2Paperless/MainWindow.xaml Naps2Paperless/MainWindow.xaml.cs
git commit -m "feat: wire up MainWindow navigation between scan and settings views"
```

---

### Task 11: Build and Verify

- [ ] **Step 1: Build the project**

Run: `dotnet build Naps2Paperless.sln`
Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Fix any build issues**

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve build issues"
```
