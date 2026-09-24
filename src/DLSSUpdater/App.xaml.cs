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
        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task failed", args.Exception);
            args.SetObserved();
        };
        base.OnStartup(e);
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
