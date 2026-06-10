using FrpManager.Helpers;
using System.Windows;

namespace FrpManager.Views
{
    /// <summary>
    /// Manages the Windows system tray (notification area) icon for FrpManager.
    /// Provides a context menu with Show/Start/Stop/Exit commands.
    /// Handles minimize-to-tray behavior — when the user closes the window,
    /// it hides to the tray instead of terminating the application.
    /// </summary>
    public class TrayIconManager : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly System.Windows.Forms.ContextMenuStrip _menu;
        private readonly System.Windows.Forms.ToolStripMenuItem _itemShow;
        private readonly System.Windows.Forms.ToolStripMenuItem _itemFrpc;
        private readonly System.Windows.Forms.ToolStripMenuItem _itemExit;
        private readonly Window _owner;
        private readonly LocalizationService _loc;
        private readonly System.Windows.Threading.DispatcherTimer? _retryTimer;
        private bool _frpcRunning;
        private bool _disposed;

        /// <summary>Fired when the user requests to show the main window.</summary>
        public event Action? ShowWindowRequested;

        /// <summary>Fired when the user requests to toggle frpc start/stop.</summary>
        public event Action? ToggleFrpcRequested;

        /// <summary>Fired when the user requests to exit the application.</summary>
        public event Action? ExitRequested;

        /// <summary>
        /// Creates the tray icon manager and initializes the system tray icon.
        /// The icon is immediately visible after construction.
        /// Includes a startup retry timer to handle cases where the taskbar
        /// notification area isn't ready yet (e.g., during Windows auto-start).
        /// </summary>
        /// <param name="owner">The main window to control show/hide for.</param>
        /// <param name="loc">Localization service for menu text.</param>
        public TrayIconManager(Window owner, LocalizationService loc)
        {
            _owner = owner;
            _loc = loc;

            // Refresh menu labels when language changes
            loc.LanguageChanged += RefreshLabels;

            // ── Minimize button → hide to tray instead of minimizing to taskbar ──
            _owner.StateChanged += (_, _) =>
            {
                if (_owner.WindowState == WindowState.Minimized)
                {
                    _owner.Hide();
                    _owner.ShowInTaskbar = false;
                }
            };

            // ── Build context menu items ──
            _itemShow = new System.Windows.Forms.ToolStripMenuItem();
            _itemShow.Click += (_, _) => ShowWindowRequested?.Invoke();

            _itemFrpc = new System.Windows.Forms.ToolStripMenuItem();
            _itemFrpc.Click += (_, _) => ToggleFrpcRequested?.Invoke();

            _itemExit = new System.Windows.Forms.ToolStripMenuItem();
            _itemExit.Click += (_, _) => ExitRequested?.Invoke();

            // ── Assemble menu ──
            _menu = new System.Windows.Forms.ContextMenuStrip();
            _menu.Items.Add(_itemShow);
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add(_itemFrpc);
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add(_itemExit);

            // ── Load app icon (with fallback chain) ──
            System.Drawing.Icon? appIcon = null;
            var iconPath = AppDirHelper.AppIconPath;
            try
            {
                if (System.IO.File.Exists(iconPath))
                    appIcon = new System.Drawing.Icon(iconPath);
            }
            catch
            {
                // Icon file corrupt or unreadable — fall through to fallback
            }
            appIcon ??= System.Drawing.SystemIcons.Application;

            // ── Create tray icon ──
            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = appIcon,
                Text = "FrpManager",
                ContextMenuStrip = _menu,
                Visible = true
            };

            // Double-click tray icon → show the main window
            _icon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke();

            // Set initial menu labels
            RefreshLabels();

            // ── Startup retry timer ──
            // During Windows auto-start, the taskbar notification area may not
            // be ready yet. This timer re-asserts icon visibility every 3 seconds
            // for the first ~15 seconds to handle the timing issue.
            int retryCount = 0;
            _retryTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background,
                _owner.Dispatcher)
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _retryTimer.Tick += (_, _) =>
            {
                retryCount++;
                if (retryCount > 5)
                {
                    _retryTimer.Stop();
                    return;
                }
                // Toggle visibility to force re-registration with Shell_NotifyIcon
                _icon.Visible = false;
                _icon.Visible = true;
            };
            _retryTimer.Start();
        }

        /// <summary>
        /// Updates the frpc start/stop menu item text based on running state.
        /// Called by MainWindow when frpc starts or stops.
        /// </summary>
        /// <param name="running">True if frpc is currently running.</param>
        public void SetFrpcRunning(bool running)
        {
            _frpcRunning = running;
            _itemFrpc.Text = running
                ? _loc.Get("S_TrayStopFrpc")
                : _loc.Get("S_TrayStartFrpc");
        }

        /// <summary>
        /// Shows a balloon tip notification near the tray icon.
        /// Used to inform the user that the app is still running after minimizing.
        /// </summary>
        /// <param name="title">Balloon title.</param>
        /// <param name="text">Balloon body text.</param>
        public void ShowBalloon(string title, string text)
        {
            // ShowBalloonTip may fail if the icon is not fully initialized on some systems;
            // catch and ignore to prevent crashes
            try
            {
                _icon.ShowBalloonTip(3000, title, text,
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch { /* Icon not ready — non-critical, skip the balloon */ }
        }

        /// <summary>
        /// Hides the main window and shows only the tray icon.
        /// Sets the window to minimized state and removes it from the taskbar.
        /// Toggles tray icon visibility to force re-registration with the
        /// notification area in case the taskbar was restarted.
        /// </summary>
        public void HideToTray()
        {
            _owner.WindowState = WindowState.Minimized;
            _owner.ShowInTaskbar = false;
            _owner.Hide();

            // Toggle visibility to force Shell_NotifyIcon re-registration.
            // This handles cases where the taskbar was restarted while the
            // app was running, which would otherwise cause the icon to vanish.
            _icon.Visible = false;
            _icon.Visible = true;
        }

        /// <summary>
        /// Restores the main window from the tray.
        /// Shows the window, restores normal state, and brings it to the foreground.
        /// </summary>
        public void ShowWindow()
        {
            _owner.Show();
            _owner.WindowState = WindowState.Normal;
            _owner.ShowInTaskbar = true;
            _owner.Activate(); // Bring window to front
        }

        /// <summary>
        /// Refreshes all tray icon menu labels to match the current language.
        /// Called when the user switches between Chinese and English.
        /// </summary>
        private void RefreshLabels()
        {
            _itemShow.Text = _loc.Get("S_TrayShow");
            _itemFrpc.Text = _frpcRunning
                ? _loc.Get("S_TrayStopFrpc")
                : _loc.Get("S_TrayStartFrpc");
            _itemExit.Text = _loc.Get("S_TrayExit");
            _icon.Text = "FrpManager";
        }

        /// <summary>
        /// Disposes the tray icon, menu, and event subscriptions.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _retryTimer?.Stop();
            _loc.LanguageChanged -= RefreshLabels;
            _icon.Visible = false;
            _icon.Dispose();
            // Dispose menu safely — may still be in use if exit was triggered
            // from a menu item click (deferred by caller via BeginInvoke)
            try { _menu.Dispose(); } catch { /* Already disposed or in use */ }
        }
    }
}
