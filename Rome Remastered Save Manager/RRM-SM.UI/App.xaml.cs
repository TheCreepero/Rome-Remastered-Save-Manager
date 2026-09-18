using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace RRM_SM.UI
{
    public partial class App : System.Windows.Application
    {
        private const string AppMutexName = "Local\\RomeRemasteredSaveManager_SingleInstance_Mutex";
        private const string ShowEventName = "Local\\RomeRemasteredSaveManager_ShowWindow_Event";

        private Mutex? _appMutex;
        private EventWaitHandle? _showEvent;
        private Thread? _signalListenerThread;
        private volatile bool _isDisposing;

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                    File.WriteAllText(logPath, e.ExceptionObject?.ToString() ?? "Unknown exception");
                }
                catch { }
            };

            DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                    File.WriteAllText(logPath, e.Exception.ToString());
                }
                catch { }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isFirstInstance;
            try
            {
                _appMutex = new Mutex(true, AppMutexName, out isFirstInstance);
            }
            catch (AbandonedMutexException)
            {
                isFirstInstance = true;
            }
            catch
            {
                isFirstInstance = true;
            }

            if (!isFirstInstance)
            {
                // Signal the running instance to bring its window to the front
                try
                {
                    using var signalEvent = EventWaitHandle.OpenExisting(ShowEventName);
                    signalEvent.Set();
                }
                catch
                {
                    // Fall back quietly if event cannot be signaled
                }

                // Terminate this secondary duplicate instance
                Shutdown(0);
                return;
            }

            // Primary instance: listen for wake/show signals from subsequent launches
            try
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                _signalListenerThread = new Thread(ListenForShowSignals)
                {
                    IsBackground = true,
                    Name = "SingleInstanceSignalListener"
                };
                _signalListenerThread.Start();
            }
            catch
            {
                // Ignore if handle creation fails
            }

            base.OnStartup(e);
        }

        private void ListenForShowSignals()
        {
            while (!_isDisposing && _showEvent != null)
            {
                try
                {
                    if (_showEvent.WaitOne())
                    {
                        if (_isDisposing) break;
                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is Window win)
                            {
                                win.Show();
                                if (win.WindowState == WindowState.Minimized)
                                {
                                    win.WindowState = WindowState.Normal;
                                }
                                win.Activate();
                                win.Topmost = true;
                                win.Topmost = false;
                                win.Focus();
                            }
                        });
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isDisposing = true;
            try
            {
                _showEvent?.Set();
                _showEvent?.Dispose();
            }
            catch { }

            if (_appMutex != null)
            {
                try { _appMutex.ReleaseMutex(); } catch { }
                _appMutex.Dispose();
            }

            base.OnExit(e);
        }
    }
}

