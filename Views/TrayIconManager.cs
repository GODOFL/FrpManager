using FrpManager.Helpers;
using System.Windows;

namespace FrpManager.Views
{
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

        public event Action? ShowWindowRequested;
        public event Action? ToggleFrpcRequested;
        public event Action? ExitRequested;

        public TrayIconManager(Window owner, LocalizationService loc)
        {
            _owner = owner;
            _loc = loc;
            loc.LanguageChanged += RefreshLabels;

            _itemShow = new System.Windows.Forms.ToolStripMenuItem();
            _itemShow.Click += (_, _) => ShowWindowRequested?.Invoke();

            _itemFrpc = new System.Windows.Forms.ToolStripMenuItem();
            _itemFrpc.Click += (_, _) => ToggleFrpcRequested?.Invoke();

            _itemExit = new System.Windows.Forms.ToolStripMenuItem();
            _itemExit.Click += (_, _) => ExitRequested?.Invoke();

            _menu = new System.Windows.Forms.ContextMenuStrip();
            _menu.Items.Add(_itemShow);
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add(_itemFrpc);
            _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            _menu.Items.Add(_itemExit);

            // Use the app's embedded icon
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            var appIcon = System.IO.File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath)
                : System.Drawing.SystemIcons.Application;

            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = appIcon,
                Text = "FrpManager",
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (_, _) => ShowWindowRequested?.Invoke();

            RefreshLabels();
        }

        public void SetFrpcRunning(bool running)
        {
            _frpcRunning = running;
            _itemFrpc.Text = running
                ? _loc.Get("S_TrayStopFrpc")
                : _loc.Get("S_TrayStartFrpc");
        }

        public void ShowBalloon(string title, string text)
        {
            _icon.ShowBalloonTip(3000, title, text,
                System.Windows.Forms.ToolTipIcon.Info);
        }

        public void HideToTray()
        {
            _owner.WindowState = WindowState.Minimized;
            _owner.ShowInTaskbar = false;
            _owner.Hide();
        }

        public void ShowWindow()
        {
            _owner.Show();
            _owner.WindowState = WindowState.Normal;
            _owner.ShowInTaskbar = true;
            _owner.Activate();
        }

        private void RefreshLabels()
        {
            _itemShow.Text = _loc.Get("S_TrayShow");
            _itemFrpc.Text = _frpcRunning
                ? _loc.Get("S_TrayStopFrpc")
                : _loc.Get("S_TrayStartFrpc");
            _itemExit.Text = _loc.Get("S_TrayExit");
            _icon.Text = "FrpManager";
        }

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
