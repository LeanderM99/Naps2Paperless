# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
dotnet build Naps2Paperless/Naps2Paperless.csproj
dotnet run --project Naps2Paperless/Naps2Paperless.csproj
```

Target framework: .NET 10.0 Windows (WPF). No external NuGet packages.

## Testing

No test project exists. If adding tests, use a separate xUnit project targeting the ViewModels and Services.

## Architecture

**WPF app using MVVM** that wraps NAPS2 (scanner CLI) and uploads scanned PDFs to a Paperless-ngx instance via HTTP API.

### Layer structure

- **Models/** — `AppSettings` POCO (NAPS2 path, profile name, API URL, API token)
- **Services/** — `ScanService` (runs NAPS2.Console.exe as subprocess, uploads PDFs) and `SettingsService` (JSON persistence to `settings.json` beside the executable)
- **ViewModels/** — `ScanViewModel`, `SettingsViewModel`, plus `BaseViewModel` (INotifyPropertyChanged) and `RelayCommand` (ICommand)
- **Views/** — `ScanView.xaml`, `SettingsView.xaml` with minimal codebehind
- **MainWindow** — Navigation host using ContentControl; views swapped via events (no framework router)

### Key flows

- **Scanning:** ScanViewModel validates API token → ScanService launches `NAPS2.Console.exe` with profile/source/split args → captures stdout/stderr → uploads resulting PDFs via multipart POST → cleans up temp files
- **Manual duplex:** Two-pass scan with a MessageBox prompt between passes for the user to flip the paper stack
- **Split modes:** "single" (no split), "perpage" (`--split`), "patcht" (`--splitpatcht`)
- **Scan sources:** glass, feeder, duplex, manualduplex

### Navigation

Event-driven: `ScanViewModel.NavigateToSettingsRequested` / `SettingsViewModel.NavigateBackRequested` are wired in `MainWindow.xaml.cs` to swap the ContentControl content.

## Conventions

- UI strings are in **German** ("Einstellungen", "Speichern & Zurueck", etc.)
- All I/O is async with CancellationToken support
- Cross-thread UI updates use `Application.Current.Dispatcher.Invoke`
- PasswordBox binding is handled manually in codebehind (WPF limitation)
