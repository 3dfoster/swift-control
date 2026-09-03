using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace SwiftControl
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Local\SwiftControl.Instance";
        private const string ShowEventName = @"Local\SwiftControl.Show";

        [STAThread]
        private static void Main(string[] args)
        {
            bool startHidden = IsStartupLaunch(args);
            bool firstInstance;
            using (Mutex instance = new Mutex(true, InstanceMutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    if (!startHidden) SignalRunningInstance();
                    return;
                }

                try
                {
                    RunApplication(startHidden);
                }
                finally
                {
                    instance.ReleaseMutex();
                }
            }
        }

        private static void RunApplication(bool startHidden)
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            MainWindow window = new MainWindow();
            TrayController tray = new TrayController(window);
            LightingCoordinator lighting = new LightingCoordinator();
            app.Exit += delegate
            {
                lighting.Dispose();
                tray.Dispose();
            };

            bool eventCreated;
            using (EventWaitHandle showEvent = new EventWaitHandle(
                false, EventResetMode.AutoReset, ShowEventName, out eventCreated))
            {
                RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
                    showEvent,
                    delegate
                    {
                        try { app.Dispatcher.BeginInvoke(new Action(window.ShowFromTray)); }
                        catch (InvalidOperationException) { }
                    },
                    null,
                    Timeout.Infinite,
                    false);

                try
                {
                    bool codexCreated;
                    using (EventWaitHandle codexEvent = new EventWaitHandle(
                        false, EventResetMode.AutoReset,
                        LightingSignals.CodexComplete, out codexCreated))
                    {
                        RegisteredWaitHandle codexRegistration = ThreadPool.RegisterWaitForSingleObject(
                            codexEvent, delegate { lighting.NotifyCodexComplete(); }, null,
                            Timeout.Infinite, false);

                        try
                        {
                            if (startHidden)
                            {
                                app.MainWindow = window;
                                app.Startup += delegate { window.StartHidden(); };
                                app.Run();
                            }
                            else
                            {
                                app.Run(window);
                            }
                        }
                        finally
                        {
                            codexRegistration.Unregister(null);
                        }
                    }
                }
                finally
                {
                    registration.Unregister(null);
                }
            }
        }

        private static bool IsStartupLaunch(string[] args)
        {
            return args.Length > 0 &&
                string.Equals(args[0], "--startup", StringComparison.OrdinalIgnoreCase);
        }

        private static void SignalRunningInstance()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using (EventWaitHandle showEvent = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        showEvent.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
