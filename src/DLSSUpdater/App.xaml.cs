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
            // Seaglass add-on: no window, JSON-RPC on stdin/stdout until Seaglass lets go.
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
        if (e.Args.Contains("--unregister-addon"))
        {
            // Run by the uninstaller: only removes the Seaglass link when it points at this exe.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { Addon.AddonRegistration.UnregisterIfThisExe(); }
            catch (Exception ex) { Log.Error("Disconnect from Seaglass", ex); }
            Shutdown();
            return;
        }
        if (e.Args.Contains("--register-addon"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { Addon.AddonRegistration.Register(); }
            catch (Exception ex) { Log.Error("Connect to Seaglass", ex); }
            Shutdown();
            return;
        }
        // Made-up games for screenshots and trying the interface; see Core/Demo.cs.
        if (e.Args.Contains("--demo")) Demo.Enable();
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
