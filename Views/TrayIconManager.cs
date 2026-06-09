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

            // ── Load app icon ──
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            var appIcon = System.IO.File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath)
                : System.Drawing.SystemIcons.Application; // Fallback to system icon

            // ── Create and show tray icon ──
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

            // Force icon visibility — some Windows versions need a re-assert
            // after construction to ensure it appears in the notification area
            _icon.Visible = true;
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
        /// Re-asserts tray icon visibility to ensure it appears reliably.
        /// </summary>
        public void HideToTray()
        {
            _owner.WindowState = WindowState.Minimized;
            _owner.ShowInTaskbar = false;
            _owner.Hide();

            // Re-assert icon visibility after window state changes.
            // On some Windows versions, hiding the window can cause the tray icon
            // to be hidden as well. This ensures the icon stays visible.
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

            _loc.LanguageChanged -= RefreshLabels;
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
