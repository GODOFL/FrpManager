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
        /// <summary>
        /// Static constructor: ensures WinForms infrastructure is initialized before
        /// any WinForms components (like NotifyIcon) are created.
        /// This is needed because the auto-generated Main() from App.xaml does not
        /// call ApplicationConfiguration.Initialize() even with UseWindowsForms=true.
        /// </summary>
        static App()
        {
            // Initialize WinForms for tray icon support.
            // EnableVisualStyles + SetCompatibleTextRenderingDefault are safe to
            // call here (before WPF Application base constructor runs).
            // We skip SetHighDpiMode because WPF handles DPI itself.
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        }
        private LocalizationService? _loc;
        private TrayIconManager? _tray;
        private SingleInstanceGuard? _singleInstance;
        private bool _isShuttingDown;

        /// <summary>
        /// Called when the application starts. Initializes localization, sets up
        /// global exception handlers, creates the main window and tray icon,
        /// and handles auto-start silent mode.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Take the mutex before creating windows or tray state. If another
            // FrpManager instance is already running, this process exits quietly.
            _singleInstance = SingleInstanceGuard.TryAcquire();
            if (_singleInstance == null)
            {
                Shutdown(0);
                return;
            }

            // ── Step 1: Load settings & initialize localization ──
            var settings = SettingsHelper.Load();
            _loc = new LocalizationService();
            _loc.Initialize(settings.Language);

            // ── Step 2: Global exception handlers ──
            // Catch unhandled UI thread exceptions (WPF dispatcher)
            DispatcherUnhandledException += (_, ex) =>
            {
                if (_isShuttingDown)
                {
                    ex.Handled = true;
                    return;
                }
                var message = ex.Exception.Message;
                if (string.IsNullOrWhiteSpace(message))
                    message = ex.Exception.GetType().Name;
                MessageBox.Show(
                    _loc.Get("S_AppErrorTitle") + ":\n\n" + message,
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
                Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    async () =>
                    {
                        if (_isShuttingDown) return;
                        _isShuttingDown = true;
                        try
                        {
                            await mainWindow.ShutdownForExitAsync();
                        }
                        finally
                        {
                            _tray?.Dispose();
                            Current.Shutdown();
                        }
                    });
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
                // This picks up the first TOML library entry as the default.
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
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
