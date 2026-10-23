using System.Windows;
using Uproot.Services;

namespace Uproot;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--selftest")) { Shutdown(SelfTest.Run()); return; }

        // One window: two batches of uninstallers at once would fight over the same apps.
        _singleInstance = new Mutex(true, @"Local\Uproot.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        AppLog.Write($"Uproot {Updater.Current} started");
        DispatcherUnhandledException += (_, args) => AppLog.Write($"Crash: {args.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Write($"Crash: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Write($"Error in background work: {args.Exception}");

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
        WhatsNewWindow.OpenOnceAfterUpdate();

        // ponytail: only an installed copy writes its card, so a build run from bin\ never tells the sisters where Uproot is;
        // the portable exe stays unknown to them too.
        if (!Updater.Portable) new Thread(() =>
        {
            try { Ecosystem.WriteCard(Environment.ProcessPath ?? "", Updater.Current); }
            catch (Exception ex) { AppLog.Write($"Couldn't write the app card: {ex.Message}"); }
        }) { IsBackground = true, Priority = ThreadPriority.Lowest }.Start();
    }
}
