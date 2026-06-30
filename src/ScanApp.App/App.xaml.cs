using System.Windows;
using System.Windows.Threading;

namespace ScanApp.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never let an unexpected error kill the app silently; show it and keep running where possible.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Something went wrong",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
