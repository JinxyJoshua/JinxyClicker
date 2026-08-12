using System.Windows;

namespace MyBlinkStyleClicker;

public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Fatal Error:\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "App Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = false;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        MessageBox.Show($"Fatal Error:\n{ex?.Message}\n\n{ex?.StackTrace}", "App Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

