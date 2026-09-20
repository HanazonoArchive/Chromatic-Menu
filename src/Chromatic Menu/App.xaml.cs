using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ChromaticMenu.Native;
using ChromaticMenu.Services;
using ChromaticMenu.ViewModels;
using ChromaticMenu.Views;

namespace ChromaticMenu
{
    public partial class App : Application
    {
        private const string MutexName = "ChromaticMenu_SingleInstance_Mutex";
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Single-instance mutex check
            bool createdNew;
            try
            {
                _mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Error("Failed to create single-instance mutex.", ex);
                createdNew = true;
            }

            if (!createdNew)
            {
                // Another instance is already running; wake and focus it, then exit
                uint msg = NativeMethods.RegisterWindowMessage("CHROMATIC_MENU_ACTIVATE");
                NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                Shutdown();
                return;
            }

            // 2. Setup global exception handlers per SPEC section 3 rule 7
            SetupExceptionHandling();

            LoggerService.Instance.Info("Chromatic Menu starting up.");
        }

        private void SetupExceptionHandling()
        {
            // UI Dispatcher unhandled exceptions
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // AppDomain unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // TaskScheduler unobserved task exceptions
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LoggerService.Instance.Error("Unhandled Dispatcher Exception caught.", e.Exception);

            // Display friendly in-app notification if MainWindow is available
            if (MainWindow?.DataContext is MainViewModel vm)
            {
                vm.ShowMessage("An unexpected error occurred: " + e.Exception.Message);
            }
            else
            {
                MessageBox.Show("An unexpected error occurred: " + e.Exception.Message,
                    "Chromatic Menu", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Keep the launcher running per SPEC section 3 rule 7
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            LoggerService.Instance.Error("Unhandled AppDomain Exception caught.", ex);
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LoggerService.Instance.Error("Unobserved Task Exception caught.", e.Exception);
            e.SetObserved();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                (MainWindow?.DataContext as MainViewModel)?.SaveConfig();
            }
            catch { }

            LoggerService.Instance.Info("Chromatic Menu exiting.");

            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore if not owned
                }
                finally
                {
                    _mutex.Dispose();
                    _mutex = null;
                }
            }

            base.OnExit(e);
        }
    }
}
