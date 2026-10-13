using System;
using System.Linq;
using System.Windows;

namespace JinxyClicker;

public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated FPS helper mode (run by the scheduled task): no window, just
        // measure Roblox's FPS over ETW and write it for the normal app to read.
        if (e.Args.Any(a => string.Equals(a, "--fps-helper", StringComparison.OrdinalIgnoreCase)))
        {
            FpsHelper.Run();
            Shutdown();
            return;
        }

        // One running copy only. A second launch hands its intent to the first and
        // exits, so the "Jinxy Play" shortcut focuses the open window (and plays)
        // instead of opening another.
        if (!SingleInstance.IsFirstInstance())
        {
            bool play = e.Args.Any(a => string.Equals(a, "--play", StringComparison.OrdinalIgnoreCase));
            SingleInstance.Signal(play ? "play" : "activate");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        SingleInstance.StartServer(message =>
            Dispatcher.Invoke(() => window.OnSecondInstance(message)));
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

