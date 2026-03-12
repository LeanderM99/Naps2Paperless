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
    private int _selectedSplitModeIndex = 1; // default "perpage"
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

    public int SelectedSplitModeIndex
    {
        get => _selectedSplitModeIndex;
        set => SetProperty(ref _selectedSplitModeIndex, value);
    }

    public string SelectedSplitMode => SplitModes[SelectedSplitModeIndex];

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
