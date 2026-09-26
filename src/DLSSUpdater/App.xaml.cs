using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DLSSUpdater.Core;

namespace DLSSUpdater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--addon"))
        {
            // WaterLauncher add-on: no window, JSON-RPC on stdin/stdout until WaterLauncher lets go.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Task.Run(async () =>
            {
                try { await Addon.AddonServer.RunStdioAsync(); }
                catch (Exception ex) { Log.Error("Add-on", ex); }
                Dispatcher.Invoke(Shutdown);
            });
            base.OnStartup(e);
            return;
        }
        if (e.Args.Contains("--register-addon"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { Addon.AddonRegistration.Register(); }
            catch (Exception ex) { Log.Error("Connect to WaterLauncher", ex); }
            Shutdown();
            return;
        }
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task failed", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled", e.Exception);
        Views.Dialog.Show("Unexpected error", e.Exception.Message);
        e.Handled = true;
    }

    public static void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
        }
    }
}
