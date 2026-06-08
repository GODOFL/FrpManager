using FrpManager.Helpers;
using FrpManager.Views;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace FrpManager
{
    public partial class App : Application
    {
        private LocalizationService? _loc;
        private TrayIconManager? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Init localization first so error messages can be localized
            var settings = SettingsHelper.Load();
            _loc = new LocalizationService();
            _loc.Initialize(settings.Language);

            // Global exception handlers
            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    _loc.Get("S_AppErrorTitle") + "：\n\n" + ex.Exception.Message,
                    "FrpManager", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                MessageBox.Show(ex.ExceptionObject?.ToString(),
                    _loc.Get("S_AppCrashTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Create and show main window
            var mainWindow = new MainWindow(_loc, settings);

            // Setup tray icon
            _tray = new TrayIconManager(mainWindow, _loc);
            _tray.ShowWindowRequested += () => _tray.ShowWindow();
            _tray.ExitRequested += () =>
            {
                mainWindow.ShutdownFrpc();
                _tray.Dispose();
                Current.Shutdown();
            };
            _tray.ToggleFrpcRequested += () => mainWindow.ToggleFrpc();
            mainWindow.SetTrayIcon(_tray);

            // If launched via auto-start, start minimized to tray
            if (AutoStartHelper.IsAutoStartLaunch())
            {
                mainWindow.Loaded += (_, _) =>
                {
                    _tray.HideToTray();
                    // Resume frpc if it was running last session
                    if (settings.FrpcWasRunning)
                        mainWindow.AutoResumeFrpc();
                };
            }

            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
