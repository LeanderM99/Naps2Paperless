using System.Windows.Controls;

namespace Naps2Paperless.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
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
