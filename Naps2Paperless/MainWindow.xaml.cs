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

        scanVm.NavigateToSettingsRequested += () =>
        {
            settingsVm.Load();
            ContentArea.Content = _settingsView;
        };
        settingsVm.NavigateBackRequested += () => ContentArea.Content = _scanView;

        ContentArea.Content = _scanView;
    }
}
