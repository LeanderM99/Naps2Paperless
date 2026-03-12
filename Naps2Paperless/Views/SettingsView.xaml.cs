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
