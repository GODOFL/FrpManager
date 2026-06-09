using FrpManager.Helpers;
using FrpManager.Views;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace FrpManager
{
    /// <summary>
    /// Application entry point. Handles startup initialization, localization loading,
    /// main window creation, tray icon setup, auto-start detection, and global exception handling.
    /// </summary>
    public partial class App : Application
    {
        private LocalizationService? _loc;
        private TrayIconManager? _tray;

        /// <summary>
        /// Called when the application starts. Initializes localization, sets up
        /// global exception handlers, creates the main window and tray icon,
        /// and handles auto-start silent mode.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Step 1: Load settings & initialize localization ──
            var settings = SettingsHelper.Load();
            _loc = new LocalizationService();
            _loc.Initialize(settings.Language);

            // ── Step 2: Global exception handlers ──
            // Catch unhandled UI thread exceptions (WPF dispatcher)
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    _loc.Get("S_AppErrorTitle") + "：\n\n" + ex.Exception.Message,
                    "FrpManager", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true; // Prevent app crash
            };

            // Catch unhandled background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                MessageBox.Show(ex.ExceptionObject?.ToString(),
                    _loc.Get("S_AppCrashTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // ── Step 3: Create main window ──
            var mainWindow = new MainWindow(_loc, settings);

            // ── Step 4: Setup tray icon (always visible, even in silent mode) ──
            _tray = new TrayIconManager(mainWindow, _loc);
            _tray.ShowWindowRequested += () => _tray.ShowWindow();
            _tray.ExitRequested += () =>
            {
                // Graceful shutdown: stop frpc, dispose tray, then exit
                mainWindow.ShutdownFrpc();
                _tray.Dispose();
                Current.Shutdown();
            };
            _tray.ToggleFrpcRequested += () => mainWindow.ToggleFrpc();
            mainWindow.SetTrayIcon(_tray);

            // ── Step 5: Auto-start or normal launch ──
            if (AutoStartHelper.IsAutoStartLaunch())
            {
                // Silent background mode: hide window, show only tray icon.
                // The tray icon is already visible from construction; HideToTray()
                // re-asserts visibility after the window state change.
                _tray.HideToTray();

                // If frpc was running when the app last closed, auto-resume it.
                // This picks up the first config (Order=1) as the default.
                if (settings.FrpcWasRunning)
                    mainWindow.AutoResumeFrpc();
            }
            else
            {
                // Normal launch: show the main window
                mainWindow.Show();
            }
        }

        /// <summary>
        /// Called when the application is exiting. Ensures the tray icon is properly disposed.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
