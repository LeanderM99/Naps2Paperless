using System.Windows.Input;
using Naps2Paperless.Services;

namespace Naps2Paperless.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly SettingsService _settingsService;
    private string _naps2Path = "";
    private string _profileName = "";
    private string _apiBaseUrl = "";
    private string _apiEndpoint = "";
    private string _apiToken = "";

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        SaveCommand = new RelayCommand(_ => Save());
        BackCommand = new RelayCommand(_ => NavigateBackRequested?.Invoke());
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

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => SetProperty(ref _apiBaseUrl, value);
    }

    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set => SetProperty(ref _apiEndpoint, value);
    }

    public string ApiToken
    {
        get => _apiToken;
        set => SetProperty(ref _apiToken, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand BrowseNaps2Command { get; }

    public event Action? NavigateBackRequested;

    public void Load()
    {
        var s = _settingsService.Load();
        Naps2Path = s.Naps2Path;
        ProfileName = s.ProfileName;
        ApiBaseUrl = s.ApiBaseUrl;
        ApiEndpoint = s.ApiEndpoint;
        ApiToken = s.ApiToken;
    }

    private void Save()
    {
        _settingsService.Save(new Models.AppSettings
        {
            Naps2Path = Naps2Path,
            ProfileName = ProfileName,
            ApiBaseUrl = ApiBaseUrl,
            ApiEndpoint = ApiEndpoint,
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
