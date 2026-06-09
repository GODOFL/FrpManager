using FrpManager.Helpers;
using FrpManager.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tomlyn;
using Tomlyn.Model;
// WinForms/WPF type conflict resolution — both have same-named types
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace FrpManager.Views
{
    /// <summary>
    /// Main application window for FrpManager.
    /// Provides the complete UI for editing FRP proxy/visitor configurations,
    /// managing frpc lifecycle, previewing/exporting TOML configs,
    /// and downloading FRP binaries from GitHub.
    /// </summary>
    public partial class MainWindow : Window
    {
        // ── Dependencies ────────────────────────────────────────────────────
        private readonly LocalizationService _loc;
        private TrayIconManager? _tray;

        // ── State ───────────────────────────────────────────────────────────
        private readonly ServerConfig _server = new();
        private readonly ObservableCollection<ProxyConfig> _proxies = new();
        private readonly ObservableCollection<VisitorConfig> _visitors = new();
        private AppSettings _settings;

        private ProxyConfig? _curProxy;
        private VisitorConfig? _curVisitor;
        private bool _busy; // Guards against event handlers firing during programmatic UI updates

        private readonly FrpcProcessManager _frpc = new();
        private TerminalWriter? _term;
        private CancellationTokenSource? _dlCts;

        /// <summary>Last saved or loaded TOML file path. Used to enable direct overwrite on Save.</summary>
        private string? _lastFilePath;

        // ── Shorthand ───────────────────────────────────────────────────────
        /// <summary>Shortcut for localization lookup.</summary>
        string L(string key) => _loc.Get(key);

        // ══ Constructor ═════════════════════════════════════════════════════

        /// <summary>
        /// Initializes the main window. Sets up data bindings, event handlers,
        /// terminal output, and the initial UI state.
        /// </summary>
        /// <param name="loc">Localization service for multi-language support.</param>
        /// <param name="settings">Persisted application settings.</param>
        public MainWindow(LocalizationService loc, AppSettings settings)
        {
            _loc = loc;
            _settings = settings;
            _busy = true; // Block events during initialization to prevent cascading triggers
            InitializeComponent();

            // ── Set window icon (taskbar) ──
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (System.IO.File.Exists(iconPath))
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath));

            // ── Initialize terminal writer ──
            _term = new TerminalWriter(TerminalBox);

            // ── Bind list data sources ──
            ProxyList.ItemsSource = _proxies;
            VisitorList.ItemsSource = _visitors;

            // ── Restore last saved file path from settings ──
            _lastFilePath = _settings.LastSavedFilePath;

            // ── Wire language change event ──
            // When the user toggles language, refresh all dynamic labels
            _loc.LanguageChanged += () =>
            {
                UpdateCounts();
                RefreshDynamicLabels();
            };

            // ── Wire frpc process events (fire on background threads) ──
            // Use BeginInvoke (async) to prevent deadlock: Stop() kills the process
            // and Dispose() waits for Exited handlers; if those handlers use
            // synchronous Invoke, they deadlock with the UI thread.
            _frpc.LineReceived += (line, isStderr) =>
                Dispatcher.BeginInvoke(() => _term?.AppendLine(line, isStderr));
            _frpc.ProcessExited += (code) =>
                Dispatcher.BeginInvoke(() =>
                {
                    SetFrpRunning(false);
                    _term?.Append(L("S_TermExited") + code + " )───", TerminalWriter.BrushMuted);
                    SetStatus(L("S_StatusExited"));
                });

            // ── Load initial UI state ──
            LoadServerToUI();
            LoadFrpcPathsToUI();
            LoadAutoStartUI();
            RefreshPreview();
            SetStatus(L("S_Ready"));

            // ── Tab switch handler: refresh TOML library on tab select ──
            MainTabs.SelectionChanged += (_, _) =>
            {
                if (MainTabs.SelectedItem == TabTomlLib)
                    LoadTomlFileList();
            };

            _busy = false;

            // ── Sync initial ComboBox state (was blocked by _busy during InitializeComponent) ──
            SyncServerAuthUI();
        }

        // ══ Tray integration ════════════════════════════════════════════════

        /// <summary>Sets the tray icon manager reference (called by App.xaml.cs).</summary>
        public void SetTrayIcon(TrayIconManager tray) => _tray = tray;

        /// <summary>
        /// Gracefully shuts down the frpc process on a background thread.
        /// This is called before app exit to clean up the running frpc instance.
        /// </summary>
        public void ShutdownFrpc()
        {
            // Offload to background thread to avoid deadlocking the UI thread
            // (Stop() → Kill() → Dispose() waits for Exited event, whose handler
            //  uses Dispatcher.BeginInvoke — safe now, but Task.Run avoids any
            //  chance of blocking the UI)
            _ = Task.Run(() => _frpc.Stop());
        }

        /// <summary>Toggles frpc start/stop (called from tray menu).</summary>
        public void ToggleFrpc() => Btn_Start(this, new RoutedEventArgs());

        /// <summary>
        /// Auto-resumes frpc on system startup (silent mode).
        /// The first config (Order=1) becomes the default selected config.
        /// </summary>
        public void AutoResumeFrpc()
        {
            if (!string.IsNullOrWhiteSpace(_settings.FrpcPath)
                && File.Exists(_settings.FrpcPath))
            {
                Btn_Start(this, new RoutedEventArgs());

                // ── Select the first config (Order=1) as default ──
                // This ensures the primary config is highlighted in the editor
                // when the app starts silently in the background.
                if (_proxies.Count > 0)
                {
                    var firstProxy = _proxies.OrderBy(p => p.Order).First();
                    ProxyList.SelectedItem = firstProxy;
                    SetStatus(L("S_FirstConfigLoaded") + ": " + firstProxy.Name);
                }
            }
        }

        // ══ Language toggle ═════════════════════════════════════════════════

        /// <summary>
        /// Toggles the UI language between zh-CN (Chinese) and en-US (English).
        /// Persists the choice to settings.
        /// </summary>
        void Btn_ToggleLang(object s, RoutedEventArgs e)
        {
            var newLang = _loc.Toggle();
            _settings.Language = newLang;
            SettingsHelper.Save(_settings);
        }

        /// <summary>
        /// Refreshes all dynamic UI labels that change based on current language
        /// or frpc running state.
        /// </summary>
        void RefreshDynamicLabels()
        {
            bool running = _frpc.IsRunning;
            TxtFrpStatus.Text = running ? L("S_FrpcRunning") : L("S_FrpcNotRunning");
            TxtTermStatus.Text = running
                ? $"{L("S_FrpcRunning")} (PID {_frpc.ProcessId})"
                : L("S_FrpcNotRunning");
            TxtStartLabel.Text = running ? L("S_StopFrpc") : L("S_StartFrpc");
            TxtStatus.Text = L("S_Ready");
            UpdateFrpcHint();
        }

        // ══ Auto-Start ══════════════════════════════════════════════════════

        /// <summary>Loads the auto-start toggle state from settings without triggering events.</summary>
        void LoadAutoStartUI()
        {
            _busy = true;
            ChkAutoStart.IsChecked = _settings.AutoStartEnabled;
            _busy = false;
        }

        /// <summary>
        /// Handles auto-start toggle changes. Enables or disables the Windows
        /// registry Run entry and persists the setting.
        /// </summary>
        void AutoStart_Changed(object s, RoutedEventArgs e)
        {
            if (_busy || !IsLoaded) return;
            bool enabled = ChkAutoStart.IsChecked == true;
            _settings.AutoStartEnabled = enabled;
            SettingsHelper.Save(_settings);
            if (enabled)
                AutoStartHelper.Enable();
            else
                AutoStartHelper.Disable();
        }

        // ══ FRP Path ════════════════════════════════════════════════════════

        /// <summary>Populates the frpc path combo box from recent paths.</summary>
        void LoadFrpcPathsToUI()
        {
            _busy = true;
            CmbFrpcPath.Items.Clear();
            foreach (var p in _settings.RecentFrpcPaths)
                CmbFrpcPath.Items.Add(p);
            if (!string.IsNullOrWhiteSpace(_settings.FrpcPath))
                CmbFrpcPath.Text = _settings.FrpcPath;
            UpdateFrpcHint();
            _busy = false;
        }

        /// <summary>Handles frpc path changes from the combo box.</summary>
        void FrpcPath_Changed(object s, SelectionChangedEventArgs e)
        {
            if (_busy) return;
            _settings.FrpcPath = CmbFrpcPath.Text;
            SettingsHelper.Save(_settings);
            UpdateFrpcHint();
        }

        /// <summary>
        /// Updates the frpc path hint text and color based on whether the file exists.
        /// Green = valid, Red = missing, Gray = not set.
        /// </summary>
        void UpdateFrpcHint()
        {
            var path = CmbFrpcPath.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                TxtFrpcPathHint.Text = L("S_FrpcPathHintEmpty");
                TxtFrpcPathHint.Foreground = (Brush)FindResource("TextMutedBrush");
            }
            else if (File.Exists(path))
            {
                TxtFrpcPathHint.Text = L("S_FrpcPathHintOk");
                TxtFrpcPathHint.Foreground = (Brush)FindResource("AccentGreenBrush");
            }
            else
            {
                TxtFrpcPathHint.Text = L("S_FrpcPathHintBad");
                TxtFrpcPathHint.Foreground = (Brush)FindResource("AccentRedBrush");
            }
        }

        /// <summary>Opens a file dialog to browse for frpc.exe.</summary>
        void Btn_BrowseFrpc(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog
            {
                Title = L("S_FrpcPathSection"),
                Filter = "frpc|frpc.exe;frpc|All files|*.*"
            };
            if (d.ShowDialog() == true) SetFrpcPath(d.FileName);
        }

        /// <summary>
        /// Auto-scans common directories for frpc.exe.
        /// Falls back to checking the latest downloaded frpc version.
        /// </summary>
        void Btn_ScanFrpc(object s, RoutedEventArgs e)
        {
            SetStatus(L("S_StatusScanning"));
            // First, try the latest downloaded version
            var latest = DownloadHelper.FindLatestFrpc();
            if (latest != null)
            {
                SetFrpcPath(latest);
                SetStatus(L("S_StatusScanLatest") +
                    Path.GetFileName(Path.GetDirectoryName(latest)!));
                return;
            }
            // Fall back to broad file system scan
            var found = SettingsHelper.ScanForFrpc();
            if (found.Count == 0)
            {
                SetStatus(L("S_StatusScanNone"));
                MessageBox.Show(L("S_MsgScanNone"), L("S_MsgScanTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _busy = true;
            CmbFrpcPath.Items.Clear();
            foreach (var p in found) CmbFrpcPath.Items.Add(p);
            _busy = false;
            SetFrpcPath(found[0]);
            SetStatus($"{L("S_StatusScanDone")} {found.Count}{L("S_StatusScanUnit")}");
        }

        /// <summary>
        /// Sets the frpc path, updates recent paths list (max 8), and persists.
        /// </summary>
        void SetFrpcPath(string path)
        {
            _settings.FrpcPath = path;
            if (!_settings.RecentFrpcPaths.Contains(path))
                _settings.RecentFrpcPaths.Insert(0, path);
            // Keep only the 8 most recent paths
            if (_settings.RecentFrpcPaths.Count > 8)
                _settings.RecentFrpcPaths = _settings.RecentFrpcPaths.Take(8).ToList();
            SettingsHelper.Save(_settings);
            _busy = true;
            CmbFrpcPath.Text = path;
            _busy = false;
            UpdateFrpcHint();
        }

        // ══ Server UI ═══════════════════════════════════════════════════════

        /// <summary>Loads server config fields into the UI without triggering change events.</summary>
        void LoadServerToUI()
        {
            _busy = true;
            S_Addr.Text = _server.ServerAddr;
            S_Port.Text = _server.ServerPort.ToString();
            S_Token.Text = _server.Token;
            S_LogFile.Text = _server.LogFile;
            S_StunServer.Text = _server.NatHoleStunServer;
            _busy = false;
        }

        /// <summary>Syncs server config fields back to the model on user edits.</summary>
        void Server_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy || !IsLoaded) return;
            if (S_Addr == null || S_Port == null || S_Token == null
                || S_LogFile == null || S_LogLevel == null || S_StunServer == null)
                return;

            _server.ServerAddr = S_Addr.Text.Trim();
            _server.NatHoleStunServer = S_StunServer.Text.Trim();
            if (int.TryParse(S_Port.Text, out int p)) _server.ServerPort = p;
            _server.Token = S_Token.Text;
            _server.LogFile = S_LogFile.Text;
            if (S_LogLevel.SelectedItem is ComboBoxItem li)
                _server.LogLevel = li.Content?.ToString() ?? "info";
        }

        /// <summary>
        /// Handles auth method combo box changes.
        /// Shows/hides the token input panel based on selected method.
        /// </summary>
        void Server_AuthChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy || !IsLoaded) return;
            if (S_AuthMethod.SelectedItem is not ComboBoxItem ci) return;
            var raw = ci.Content?.ToString() ?? "";
            // Parse localized auth method labels back to canonical names
            var method = raw.Contains("none") || raw.Contains("不认证") || raw == "none (no auth)"
                ? "none"
                : raw.Contains("oidc") ? "oidc" : "token";
            _server.AuthMethod = method;
            TokenPanel.Visibility = method == "token"
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Syncs the auth method UI state after initialization.</summary>
        void SyncServerAuthUI()
        {
            if (S_AuthMethod.SelectedItem is ComboBoxItem ci)
            {
                var raw = ci.Content?.ToString() ?? "";
                var method = raw.Contains("none") || raw.Contains("不认证") || raw == "none (no auth)"
                    ? "none"
                    : raw.Contains("oidc") ? "oidc" : "token";
                _server.AuthMethod = method;
                TokenPanel.Visibility = method == "token"
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>Handles TLS checkbox changes.</summary>
        void Server_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy || !IsLoaded) return;
            _server.TlsEnable = S_Tls.IsChecked == true;
        }

        /// <summary>Opens a save dialog for the log file path.</summary>
        void Btn_BrowseLog(object s, RoutedEventArgs e)
        {
            var d = new SaveFileDialog { Filter = "Log|*.log|All|*.*", FileName = "frpc.log" };
            if (d.ShowDialog() == true) S_LogFile.Text = d.FileName;
        }

        // ══ Proxy CRUD ══════════════════════════════════════════════════════

        /// <summary>
        /// Adds a new proxy config to the list and selects it for editing.
        /// Automatically assigns the next available Order number.
        /// </summary>
        void Btn_AddProxy(object s, RoutedEventArgs e)
        {
            var p = new ProxyConfig
            {
                Name = $"proxy-{_proxies.Count + 1}",
                Order = _proxies.Count + 1 // Assign next order number
            };
            _proxies.Add(p);
            VisitorList.SelectedItem = null;
            ProxyList.SelectedItem = p;
            UpdateCounts();
        }

        /// <summary>Deletes a proxy config. Removes from the collection and clears the editor if it was selected.</summary>
        void Btn_DeleteProxy(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is ProxyConfig p)
            {
                _proxies.Remove(p);
                if (_curProxy == p) { _curProxy = null; ShowEditor(EditorMode.None); }
                RenumberProxies(); // Re-number remaining items
                UpdateCounts();
                SetStatus(L("S_DeletedProxy") + p.Name);
            }
        }

        /// <summary>Handles proxy list selection changes. Loads the selected proxy into the editor.</summary>
        void ProxyList_Changed(object s, SelectionChangedEventArgs e)
        {
            if (ProxyList.SelectedItem is not ProxyConfig p) return;
            _curProxy = p;
            _curVisitor = null;
            VisitorList.SelectedItem = null;
            LoadProxyToUI(p);
            ShowEditor(EditorMode.Proxy);
            TxtProxyTitle.Text = p.Name;
        }

        /// <summary>Populates the proxy editor fields from a ProxyConfig model.</summary>
        void LoadProxyToUI(ProxyConfig p)
        {
            _busy = true;
            F_Name.Text = p.Name;
            F_LocalIp.Text = p.LocalIp;
            F_LocalPort.Text = p.LocalPort.ToString();
            F_RemotePort.Text = p.RemotePort.ToString();
            F_Domains.Text = p.CustomDomains;
            F_Subdomain.Text = p.Subdomain;
            F_Sk.Text = p.Sk;
            F_Encrypt.IsChecked = p.UseEncryption;
            F_Compress.IsChecked = p.UseCompression;
            // Select matching type in combo box
            foreach (ComboBoxItem item in F_Type.Items)
                if (item.Content?.ToString() == p.Type.ToString())
                { F_Type.SelectedItem = item; break; }
            _busy = false;
            ApplyProxyTypeVisibility(p.Type);
        }

        /// <summary>Syncs proxy editor fields back to the selected ProxyConfig model in real-time.</summary>
        void Proxy_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curProxy == null) return;
            _curProxy.Name = F_Name.Text;
            _curProxy.LocalIp = F_LocalIp.Text;
            _curProxy.CustomDomains = F_Domains.Text;
            _curProxy.Subdomain = F_Subdomain.Text;
            _curProxy.Sk = F_Sk.Text;
            if (int.TryParse(F_LocalPort.Text, out int lp)) _curProxy.LocalPort = lp;
            if (int.TryParse(F_RemotePort.Text, out int rp)) _curProxy.RemotePort = rp;
            TxtProxyTitle.Text = _curProxy.Name;
            RefreshList(ProxyList);
        }

        /// <summary>Handles proxy type changes. Updates type-dependent UI card visibility.</summary>
        void Proxy_TypeChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy || _curProxy == null) return;
            if (F_Type.SelectedItem is ComboBoxItem ci &&
                Enum.TryParse<ProxyType>(ci.Content?.ToString(), out var t))
            {
                _curProxy.Type = t;
                ApplyProxyTypeVisibility(t);
                RefreshList(ProxyList);
            }
        }

        /// <summary>Handles proxy encryption/compression checkbox changes.</summary>
        void Proxy_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curProxy == null) return;
            _curProxy.UseEncryption = F_Encrypt.IsChecked == true;
            _curProxy.UseCompression = F_Compress.IsChecked == true;
        }

        /// <summary>
        /// Shows/hides editor cards based on the selected proxy type.
        /// TCP/UDP → remote port card, HTTP/HTTPS → domain card, STCP/XTCP/SUDP → secret card.
        /// </summary>
        void ApplyProxyTypeVisibility(ProxyType t)
        {
            bool isHttp = t is ProxyType.http or ProxyType.https;
            bool isSecret = t is ProxyType.stcp or ProxyType.xtcp or ProxyType.sudp;
            CardRemote.Visibility = (!isHttp && !isSecret) ? Visibility.Visible : Visibility.Collapsed;
            CardHttp.Visibility = isHttp ? Visibility.Visible : Visibility.Collapsed;
            CardSecret.Visibility = isSecret ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══ Visitor CRUD ════════════════════════════════════════════════════

        /// <summary>
        /// Adds a new visitor config to the list and selects it for editing.
        /// Automatically assigns the next available Order number.
        /// </summary>
        void Btn_AddVisitor(object s, RoutedEventArgs e)
        {
            var v = new VisitorConfig
            {
                Name = $"visitor-{_visitors.Count + 1}",
                Order = _visitors.Count + 1 // Assign next order number
            };
            _visitors.Add(v);
            ProxyList.SelectedItem = null;
            VisitorList.SelectedItem = v;
            UpdateCounts();
        }

        /// <summary>Deletes a visitor config. Removes from the collection and clears the editor if it was selected.</summary>
        void Btn_DeleteVisitor(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is VisitorConfig v)
            {
                _visitors.Remove(v);
                if (_curVisitor == v) { _curVisitor = null; ShowEditor(EditorMode.None); }
                RenumberVisitors(); // Re-number remaining items
                UpdateCounts();
                SetStatus(L("S_DeletedVisitor") + v.Name);
            }
        }

        /// <summary>Handles visitor list selection changes. Loads the selected visitor into the editor.</summary>
        void VisitorList_Changed(object s, SelectionChangedEventArgs e)
        {
            if (VisitorList.SelectedItem is not VisitorConfig v) return;
            _curVisitor = v;
            _curProxy = null;
            ProxyList.SelectedItem = null;
            LoadVisitorToUI(v);
            ShowEditor(EditorMode.Visitor);
            TxtVisitorTitle.Text = v.Name;
        }

        /// <summary>Populates the visitor editor fields from a VisitorConfig model.</summary>
        void LoadVisitorToUI(VisitorConfig v)
        {
            _busy = true;
            V_Name.Text = v.Name;
            V_ServerName.Text = v.ServerName;
            V_ServerUser.Text = v.ServerUser;
            V_Sk.Text = v.Sk;
            V_BindAddr.Text = v.BindAddr;
            V_BindPort.Text = v.BindPort.ToString();
            V_KeepTunnelOpen.IsChecked = v.KeepTunnelOpen;
            V_FallbackTo.Text = v.FallbackTo;
            V_FallbackTimeoutMs.Text = v.FallbackTimeoutMs.ToString();
            // Select matching type in combo box
            foreach (ComboBoxItem item in V_Type.Items)
                if (item.Content?.ToString() == v.Type.ToString())
                { V_Type.SelectedItem = item; break; }
            _busy = false;
            ApplyVisitorTypeVisibility(v.Type);
        }

        /// <summary>Syncs visitor editor fields back to the selected VisitorConfig model in real-time.</summary>
        void Visitor_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            _curVisitor.Name = V_Name.Text;
            _curVisitor.ServerName = V_ServerName.Text;
            _curVisitor.ServerUser = V_ServerUser.Text;
            _curVisitor.Sk = V_Sk.Text;
            _curVisitor.BindAddr = V_BindAddr.Text;
            _curVisitor.FallbackTo = V_FallbackTo.Text;
            if (int.TryParse(V_BindPort.Text, out int bp)) _curVisitor.BindPort = bp;
            if (int.TryParse(V_FallbackTimeoutMs.Text, out int ftms)) _curVisitor.FallbackTimeoutMs = ftms;
            TxtVisitorTitle.Text = _curVisitor.Name;
            RefreshList(VisitorList);
        }

        /// <summary>Handles visitor type changes. Shows/hides XTCP-specific options.</summary>
        void Visitor_TypeChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            if (V_Type.SelectedItem is ComboBoxItem ci &&
                Enum.TryParse<ProxyType>(ci.Content?.ToString(), out var t))
            {
                _curVisitor.Type = t;
                ApplyVisitorTypeVisibility(t);
                RefreshList(VisitorList);
            }
        }

        /// <summary>Handles visitor keepTunnelOpen checkbox changes.</summary>
        void Visitor_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            _curVisitor.KeepTunnelOpen = V_KeepTunnelOpen.IsChecked == true;
        }

        /// <summary>Shows the XTCP card only when the visitor type is xtcp.</summary>
        void ApplyVisitorTypeVisibility(ProxyType t)
        {
            CardXtcp.Visibility = t == ProxyType.xtcp
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══ Config Reordering ─═══════════════════════════════════════════════

        /// <summary>Moves a proxy up in the list (decreases its Order number).</summary>
        void Btn_MoveProxyUp(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is ProxyConfig target)
                MoveItemUp(_proxies, target, () => RenumberProxies());
        }

        /// <summary>Moves a proxy down in the list (increases its Order number).</summary>
        void Btn_MoveProxyDown(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is ProxyConfig target)
                MoveItemDown(_proxies, target, () => RenumberProxies());
        }

        /// <summary>Moves a visitor up in the list (decreases its Order number).</summary>
        void Btn_MoveVisitorUp(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is VisitorConfig target)
                MoveItemUp(_visitors, target, () => RenumberVisitors());
        }

        /// <summary>Moves a visitor down in the list (increases its Order number).</summary>
        void Btn_MoveVisitorDown(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is VisitorConfig target)
                MoveItemDown(_visitors, target, () => RenumberVisitors());
        }

        /// <summary>
        /// Generic move-up: swaps the target item's Order with the item immediately before it.
        /// Uses the IOrderedItem interface for type-safe access (no reflection).
        /// </summary>
        /// <typeparam name="T">Item type implementing IOrderedItem.</typeparam>
        /// <param name="collection">The ObservableCollection containing the items.</param>
        /// <param name="target">The item to move up.</param>
        /// <param name="renumber">Callback to renumber and refresh after the swap.</param>
        void MoveItemUp<T>(ObservableCollection<T> collection, T target, Action renumber)
            where T : class, IOrderedItem
        {
            // Sort items by current Order to find neighbors
            var sorted = collection.OrderBy(item => item.Order).ToList();
            int idx = sorted.IndexOf(target);
            if (idx <= 0) return; // Already at the top — can't move up

            // Swap Order values with the previous item
            var prev = sorted[idx - 1];
            (prev.Order, target.Order) = (target.Order, prev.Order);

            renumber(); // Re-number all items to keep Order values clean (1,2,3...)
        }

        /// <summary>
        /// Generic move-down: swaps the target item's Order with the item immediately after it.
        /// Uses the IOrderedItem interface for type-safe access (no reflection).
        /// </summary>
        /// <typeparam name="T">Item type implementing IOrderedItem.</typeparam>
        /// <param name="collection">The ObservableCollection containing the items.</param>
        /// <param name="target">The item to move down.</param>
        /// <param name="renumber">Callback to renumber and refresh after the swap.</param>
        void MoveItemDown<T>(ObservableCollection<T> collection, T target, Action renumber)
            where T : class, IOrderedItem
        {
            var sorted = collection.OrderBy(item => item.Order).ToList();
            int idx = sorted.IndexOf(target);
            if (idx < 0 || idx >= sorted.Count - 1) return; // Already at the bottom

            // Swap Order values with the next item
            var next = sorted[idx + 1];
            (next.Order, target.Order) = (target.Order, next.Order);

            renumber(); // Re-number all items
        }

        /// <summary>
        /// Re-numbers all proxy Order values to be sequential (1, 2, 3...)
        /// based on their current sorting, then refreshes the list display.
        /// Tracks the currently selected item by reference to restore correct selection
        /// after re-sorting (the old index would point to the wrong item).
        /// </summary>
        void RenumberProxies()
        {
            // Track selected item by reference before clearing
            var selectedItem = ProxyList.SelectedItem as ProxyConfig;
            var sorted = _proxies.OrderBy(p => p.Order).ToList();
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Order = i + 1;
            // Rebuild the ObservableCollection to trigger UI refresh with new order
            _proxies.Clear();
            foreach (var p in sorted) _proxies.Add(p);
            // Restore selection by finding the item's new position after re-sorting
            ProxyList.SelectedItem = selectedItem;
            RefreshPreview();
        }

        /// <summary>
        /// Re-numbers all visitor Order values to be sequential (1, 2, 3...)
        /// based on their current sorting, then refreshes the list display.
        /// Tracks the currently selected item by reference to restore correct selection
        /// after re-sorting (the old index would point to the wrong item).
        /// </summary>
        void RenumberVisitors()
        {
            // Track selected item by reference before clearing
            var selectedItem = VisitorList.SelectedItem as VisitorConfig;
            var sorted = _visitors.OrderBy(v => v.Order).ToList();
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Order = i + 1;
            _visitors.Clear();
            foreach (var v in sorted) _visitors.Add(v);
            // Restore selection by finding the item's new position after re-sorting
            VisitorList.SelectedItem = selectedItem;
            RefreshPreview();
        }

        // ══ Editor visibility ════════════════════════════════════════════════

        /// <summary>Controls which editor panel is shown based on selection state.</summary>
        enum EditorMode { None, Proxy, Visitor }

        /// <summary>
        /// Shows the appropriate editor panel and switches to the Editor tab if needed.
        /// </summary>
        void ShowEditor(EditorMode mode)
        {
            EmptyState.Visibility = mode == EditorMode.None ? Visibility.Visible : Visibility.Collapsed;
            ProxyEditorPanel.Visibility = mode == EditorMode.Proxy ? Visibility.Visible : Visibility.Collapsed;
            VisitorEditorPanel.Visibility = mode == EditorMode.Visitor ? Visibility.Visible : Visibility.Collapsed;
            if (mode != EditorMode.None && MainTabs.SelectedItem != TabEditor)
                MainTabs.SelectedItem = TabEditor;
        }

        // ══ Templates ═══════════════════════════════════════════════════════

        /// <summary>Adds a proxy from a template and selects it.</summary>
        void AddProxy(ProxyConfig t)
        {
            t.Order = _proxies.Count + 1; // Assign order on add
            _proxies.Add(t);
            VisitorList.SelectedItem = null;
            ProxyList.SelectedItem = t;
            UpdateCounts();
            SetStatus(L("S_AddedTemplate") + t.Name);
        }

        /// <summary>Adds a visitor from a template and selects it.</summary>
        void AddVisitor(VisitorConfig v)
        {
            v.Order = _visitors.Count + 1; // Assign order on add
            _visitors.Add(v);
            ProxyList.SelectedItem = null;
            VisitorList.SelectedItem = v;
            UpdateCounts();
            SetStatus(L("S_AddedVisitorTemplate") + v.Name);
        }

        // Template click handlers — each calls AddProxy/AddVisitor with the appropriate template
        void T_Tcp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.TcpTemplate());
        void T_Ssh(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.SshTemplate());
        void T_Rdp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.RdpTemplate());
        void T_Web(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.WebTemplate());
        void T_Https(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.HttpsTemplate());
        void T_Udp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.UdpTemplate());
        void T_Mc(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.McTemplate());
        void T_Stcp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.StcpTemplate());
        void T_Xtcp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.XtcpTemplate());
        void T_Sudp(object s, RoutedEventArgs e) => AddProxy(ConfigHelper.SudpTemplate());
        void T_StcpVisitor(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.StcpVisitorTemplate());
        void T_XtcpVisitor(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.XtcpVisitorTemplate());
        void T_XtcpWithFallback(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.XtcpWithFallbackTemplate());
        void T_XtcpFallbackVisitor(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.XtcpFallbackVisitorTemplate());
        void T_SudpVisitor(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.SudpVisitorTemplate());

        // ══ Preview / Export ════════════════════════════════════════════════

        /// <summary>Regenerates the frpc.toml preview from current model state.</summary>
        void RefreshPreview()
            => PreviewBox.Text = ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors);

        void Btn_Refresh(object s, RoutedEventArgs e) => RefreshPreview();

        /// <summary>Copies the full TOML preview to the clipboard.</summary>
        void Btn_CopyAll(object s, RoutedEventArgs e)
        {
            Clipboard.SetText(PreviewBox.Text);
            SetStatus(L("S_StatusCopied"));
        }

        /// <summary>Exports frpc.toml via SaveFileDialog.</summary>
        void Btn_ExportFrpc(object s, RoutedEventArgs e)
            => ExportToml("frpc.toml", ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

        /// <summary>Exports frps.toml via SaveFileDialog.</summary>
        void Btn_ExportFrps(object s, RoutedEventArgs e)
            => ExportToml("frps.toml", ConfigHelper.GenerateFrpsToml(_server));

        /// <summary>
        /// Shows a SaveFileDialog and writes TOML content to the chosen path.
        /// ALWAYS shows a dialog — this is for explicit Export actions.
        /// Use Btn_Save for direct overwrite behavior.
        /// After saving, updates the tracked file path for subsequent quick saves.
        /// </summary>
        void ExportToml(string name, string content)
        {
            var d = new SaveFileDialog { Filter = "TOML|*.toml|All|*.*", FileName = name };
            if (d.ShowDialog() == true)
            {
                File.WriteAllText(d.FileName, content);
                _lastFilePath = d.FileName;
                _settings.LastSavedFilePath = _lastFilePath;
                SettingsHelper.Save(_settings);
                SetStatus(L("S_SavedToFile") + " " + Path.GetFileName(d.FileName));
            }
        }

        /// <summary>
        /// Opens a TOML file, parses it, and loads proxy/visitor configurations.
        /// Also sets the last file path for future direct-overwrite saves.
        /// </summary>
        void Btn_Open(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "TOML|*.toml|All|*.*" };
            if (d.ShowDialog() == true)
            {
                // Try to parse and load the TOML file
                try
                {
                    LoadTomlFile(d.FileName);
                    _lastFilePath = d.FileName;
                    _settings.LastSavedFilePath = _lastFilePath;
                    SettingsHelper.Save(_settings);
                }
                catch { /* Parse failure handled by LoadTomlFile */ }
                SetStatus(L("S_Opened") + Path.GetFileName(d.FileName) + L("S_OpenedSuffix"));
            }
        }

        /// <summary>
        /// Save button handler. If a file path is known, overwrites directly.
        /// Otherwise falls back to the Export dialog.
        /// </summary>
        void Btn_Save(object s, RoutedEventArgs e)
        {
            // Generate the current TOML content
            string content = ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors);

            // If we have a known file, overwrite directly
            if (!string.IsNullOrWhiteSpace(_lastFilePath) && File.Exists(_lastFilePath))
            {
                File.WriteAllText(_lastFilePath, content);
                SetStatus(L("S_SavedToFile") + " " + Path.GetFileName(_lastFilePath));
                return;
            }

            // Fall back to SaveFileDialog
            ExportToml("frpc.toml", content);
        }

        // ══ FRP Launch ══════════════════════════════════════════════════════

        /// <summary>
        /// Starts or stops the frpc process.
        /// Validates config, generates a temporary TOML file, and launches frpc.
        /// On stop, kills the process and cleans up.
        /// </summary>
        async void Btn_Start(object s, RoutedEventArgs e)
        {
            // ── Stop if already running ──
            if (_frpc.IsRunning)
            {
                // Run Stop() on a background thread — Kill() and Dispose() are
                // synchronous blocking calls that would freeze the UI.
                BtnStart.IsEnabled = false;   // Prevent double-click during stop
                await Task.Run(() => _frpc.Stop());

                SetFrpRunning(false);
                _term?.Append(L("S_TermStopped"), TerminalWriter.BrushMuted);
                SetStatus(L("S_StatusStopped"));
                _settings.FrpcWasRunning = false;
                SettingsHelper.Save(_settings);
                BtnStart.IsEnabled = true;
                return;
            }

            // ── Validate frpc path ──
            string frpcPath = CmbFrpcPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            {
                MessageBox.Show(L("S_MsgNoFrpcPath"), L("S_MsgNoFrpcPathTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MainTabs.SelectedItem = TabServer;
                return;
            }

            // ── Validate configuration ──
            var (valid, errors) = ConfigHelper.Validate(_server, _proxies, _visitors);
            if (!valid)
            {
                MessageBox.Show(L("S_MsgValidateFail") + "\n\n" + string.Join("\n", errors),
                    L("S_MsgValidateTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ── Generate temp TOML and launch frpc ──
            string tomlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tomlset");
            Directory.CreateDirectory(tomlDir);
            string tmp = Path.Combine(tomlDir, "frpc_mgr_tmp.toml");
            File.WriteAllText(tmp, ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

            _frpc.Start(frpcPath, tmp);

            // ── Update UI for running state ──
            SetFrpRunning(true);
            SetStatus(L("S_StatusStarted") + _frpc.ProcessId + ")");
            _term?.Append(L("S_TermStarted") + frpcPath + " ───", TerminalWriter.BrushSuccess);
            _term?.Append(L("S_TermConfig") + tmp + " ───", TerminalWriter.BrushMuted);
            MainTabs.SelectedItem = TabTerminal;

            _settings.FrpcWasRunning = true;
            SettingsHelper.Save(_settings);
        }

        /// <summary>
        /// Updates all UI elements to reflect the frpc running/stopped state.
        /// Changes status dot colors, text labels, button styles/icons.
        /// </summary>
        void SetFrpRunning(bool on)
        {
            var green = Color.FromRgb(0x52, 0xB7, 0x88);
            StatusDot.Fill = new SolidColorBrush(on ? green : Color.FromRgb(0xC0, 0xC0, 0xC0));
            TermDot.Fill = new SolidColorBrush(on ? green : Color.FromRgb(0x55, 0x55, 0x55));
            TxtFrpStatus.Text = on ? L("S_FrpcRunning") : L("S_FrpcNotRunning");
            TxtTermStatus.Text = on
                ? $"{L("S_FrpcRunning")} (PID {_frpc.ProcessId})"
                : L("S_FrpcNotRunning");
            TxtStartIcon.Text = on ? "⏹" : "▶";
            TxtStartLabel.Text = on ? L("S_StopFrpc") : L("S_StartFrpc");
            BtnStart.Style = on
                ? (Style)FindResource("DangerBtn")
                : (Style)FindResource("GreenBtn");

            _tray?.SetFrpcRunning(on);
        }

        // ══ Terminal ════════════════════════════════════════════════════════

        /// <summary>
        /// Handles terminal RichTextBox size changes.
        /// Updates the FlowDocument's PageWidth to match the viewport width,
        /// enabling proper text wrapping without horizontal scrolling.
        /// </summary>
        void TerminalBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (TerminalBox.Document != null && e.NewSize.Width > 0)
            {
                // Set PageWidth to slightly less than viewport to account for padding
                // This enables word wrap at the visible boundary
                TerminalBox.Document.PageWidth = Math.Max(1, e.NewSize.Width - 30);
            }
        }

        /// <summary>Clears all terminal output.</summary>
        void Btn_ClearTerminal(object s, RoutedEventArgs e)
            => _term?.Clear();

        // ══ Config Library ══════════════════════════════════════════════════

        void Btn_RefreshTomlList(object s, RoutedEventArgs e) => LoadTomlFileList();

        /// <summary>
        /// Loads and displays the list of TOML config files from the tomlset directory.
        /// Each file is shown as a card with metadata and load/delete buttons.
        /// </summary>
        void LoadTomlFileList()
        {
            string tomlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tomlset");
            TxtTomlDir.Text = tomlDir;
            TomlFilePanel.Children.Clear();

            if (!Directory.Exists(tomlDir))
            {
                TxtTomlHint.Text = L("S_TomlHintNoDir");
                TomlFilePanel.Children.Add(TxtTomlHint);
                return;
            }

            // Sort by last modified time, newest first
            var files = Directory.GetFiles(tomlDir, "*.toml")
                                 .OrderByDescending(File.GetLastWriteTime)
                                 .ToList();
            if (files.Count == 0)
            {
                TxtTomlHint.Text = L("S_TomlHintEmpty");
                TomlFilePanel.Children.Add(TxtTomlHint);
                return;
            }

            foreach (var file in files)
                TomlFilePanel.Children.Add(BuildTomlFileRow(file));
        }

        /// <summary>
        /// Builds a card UI element for a single TOML config file in the library view.
        /// Shows filename, modification date, size, and load/delete action buttons.
        /// </summary>
        UIElement BuildTomlFileRow(string path)
        {
            var info = new FileInfo(path);
            var modified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            var size = info.Length < 1024 ? $"{info.Length} B" : $"{info.Length / 1024.0:F1} KB";

            var border = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // File info panel (name + metadata)
            var infoPanel = new StackPanel();
            infoPanel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(path),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x50)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas")
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"{L("S_ModifiedTime")}: {modified}    {size}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(infoPanel, 0);

            // Load button
            var btnLoad = new Button
            {
                Content = L("S_Load"),
                Style = (Style)FindResource("PrimaryBtn"),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(10, 0, 0, 0),
                Tag = path
            };
            btnLoad.Click += (_, _) =>
            {
                LoadTomlFile((string)btnLoad.Tag);
                _lastFilePath = (string)btnLoad.Tag;
                _settings.LastSavedFilePath = _lastFilePath;
                SettingsHelper.Save(_settings);
            };
            Grid.SetColumn(btnLoad, 1);

            // Delete button
            var btnDel = new Button
            {
                Content = L("S_Delete"),
                Style = (Style)FindResource("DangerBtn"),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = path
            };
            btnDel.Click += (_, _) =>
            {
                var p = (string)btnDel.Tag;
                if (MessageBox.Show($"{L("S_MsgDeleteConfirm")}\n{Path.GetFileName(p)}",
                        L("S_MsgDeleteTitle"),
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    File.Delete(p);
                    // Clear last file path if we deleted the currently tracked file
                    if (string.Equals(p, _lastFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastFilePath = null;
                        _settings.LastSavedFilePath = null;
                        SettingsHelper.Save(_settings);
                    }
                    LoadTomlFileList();
                    SetStatus(L("S_StatusDeleted") + Path.GetFileName(p));
                }
            };
            Grid.SetColumn(btnDel, 2);

            grid.Children.Add(infoPanel);
            grid.Children.Add(btnLoad);
            grid.Children.Add(btnDel);
            border.Child = grid;
            return border;
        }

        /// <summary>
        /// Parses a TOML file and loads its server/proxy/visitor configuration
        /// into the application state. Supports the standard frpc.toml format.
        /// </summary>
        void LoadTomlFile(string path)
        {
            try
            {
                var text = File.ReadAllText(path);

                // Parse TOML into a table model
                TomlTable model;
                try { model = Toml.ToModel(text); }
                catch
                {
                    MessageBox.Show(L("S_MsgTomlError"), L("S_MsgTomlErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ── Server config ──
                _server.ServerAddr = model.TryGet("serverAddr") ?? _server.ServerAddr;
                if (int.TryParse(model.TryGet("serverPort"), out int p)) _server.ServerPort = p;
                _server.NatHoleStunServer = model.TryGet("natHoleStunServer") ?? "";

                // ── Auth section ──
                if (model.TryGetValue("auth", out var authObj) && authObj is TomlTable auth)
                {
                    _server.AuthMethod = auth.TryGet("method") ?? "none";
                    _server.Token = auth.TryGet("token") ?? "";
                }

                // ── Load proxies ──
                _proxies.Clear();
                if (model.TryGetValue("proxies", out var po) && po is TomlTableArray proxies)
                {
                    int order = 0;
                    foreach (TomlTable row in proxies)
                    {
                        order++;
                        _proxies.Add(new ProxyConfig
                        {
                            Order = order, // Preserve TOML array order
                            Name = row.TryGet("name") ?? "",
                            LocalIp = row.TryGet("localIP") ?? "127.0.0.1",
                            LocalPort = int.TryParse(row.TryGet("localPort"), out int lp) ? lp : 80,
                            RemotePort = int.TryParse(row.TryGet("remotePort"), out int rp) ? rp : 0,
                            CustomDomains = row.TryGet("customDomains") ?? "",
                            Subdomain = row.TryGet("subdomain") ?? "",
                            Sk = row.TryGet("secretKey") ?? "",
                            Type = Enum.TryParse<ProxyType>(row.TryGet("type"), out var pt)
                                            ? pt : ProxyType.tcp,
                        });
                    }
                }

                // ── Load visitors ──
                _visitors.Clear();
                if (model.TryGetValue("visitors", out var vo) && vo is TomlTableArray visitors)
                {
                    int order = 0;
                    foreach (TomlTable row in visitors)
                    {
                        order++;
                        _visitors.Add(new VisitorConfig
                        {
                            Order = order, // Preserve TOML array order
                            Name = row.TryGet("name") ?? "",
                            ServerName = row.TryGet("serverName") ?? "",
                            ServerUser = row.TryGet("serverUser") ?? "",
                            Sk = row.TryGet("secretKey") ?? "",
                            BindAddr = row.TryGet("bindAddr") ?? "127.0.0.1",
                            BindPort = int.TryParse(row.TryGet("bindPort"), out int bp) ? bp : 9000,
                            FallbackTo = row.TryGet("fallbackTo") ?? "",
                            FallbackTimeoutMs = int.TryParse(row.TryGet("fallbackTimeoutMs"), out int ft) ? ft : 200,
                            KeepTunnelOpen = row.TryGet("keepTunnelOpen") == "true",
                            Type = Enum.TryParse<ProxyType>(row.TryGet("type"), out var vt)
                                                ? vt : ProxyType.stcp,
                        });
                    }
                }

                // ── Refresh UI with loaded data ──
                LoadServerToUI();
                UpdateCounts();
                RefreshPreview();
                ShowEditor(EditorMode.None);
                SetStatus(L("S_StatusLoaded") + Path.GetFileName(path));
                MainTabs.SelectedItem = TabEditor;
            }
            catch (Exception ex)
            {
                MessageBox.Show(L("S_MsgLoadFailed") + ex.Message,
                    L("S_MsgLoadFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══ GitHub Download ═════════════════════════════════════════════════

        /// <summary>
        /// Fetches the latest FRP releases from GitHub and displays them as
        /// downloadable asset cards.
        /// </summary>
        async void Btn_CheckUpdate(object s, RoutedEventArgs e)
        {
            TxtRelInfo.Text = L("S_StatusConnecting");
            AssetPanel.Children.Clear();
            AssetPanel.Children.Add(MakeTextBlock(L("S_StatusLoadingList")));
            try
            {
                var releases = await GithubHelper.GetReleasesAsync();
                AssetPanel.Children.Clear();
                foreach (var rel in releases)
                {
                    // Release header with version + date
                    var hdr = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0xD8, 0xEE, 0xF8)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 8, 14, 8),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    var hSp = new StackPanel { Orientation = Orientation.Horizontal };
                    hSp.Children.Add(new TextBlock
                    {
                        Text = $"🏷  {rel.name ?? rel.tag_name}",
                        Foreground = (Brush)FindResource("AccentDarkBrush"),
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    if (rel.published_at.Length >= 10)
                        hSp.Children.Add(new TextBlock
                        {
                            Text = $"    {rel.published_at[..10]}",
                            Foreground = (Brush)FindResource("TextSecondaryBrush"),
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    hdr.Child = hSp;
                    AssetPanel.Children.Add(hdr);

                    // Asset rows (Windows binaries first)
                    foreach (var asset in rel.assets
                        .OrderByDescending(a => a.name.Contains("windows", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(a => a.name))
                        AssetPanel.Children.Add(BuildAssetRow(asset));
                    AssetPanel.Children.Add(new Border { Height = 8 });
                }
                TxtRelInfo.Text = L("S_LoadedFile") + releases.FirstOrDefault()?.tag_name;
                SetStatus(L("S_StatusUpdateDone"));
            }
            catch (Exception ex)
            {
                AssetPanel.Children.Clear();
                AssetPanel.Children.Add(MakeTextBlock(
                    L("S_StatusLoadFail") + ex.Message,
                    (Brush)FindResource("AccentRedBrush")));
                TxtRelInfo.Text = L("S_StatusUpdateFail");
            }
        }

        /// <summary>
        /// Builds a card UI element for a single GitHub release asset (downloadable file).
        /// Highlights Windows binaries with a 🪟 icon.
        /// </summary>
        UIElement BuildAssetRow(GitHubAsset asset)
        {
            bool isWin = asset.name.Contains("windows", StringComparison.OrdinalIgnoreCase);
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 2, 0, 0)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = (isWin ? "🪟 " : "") + asset.name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x50)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13
            });
            info.Children.Add(new TextBlock
            {
                Text = asset.SizeLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(info, 0);

            var btn = new Button
            {
                Content = $"⬇  {L("S_Load")}",
                Style = (Style)FindResource("PrimaryBtn"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(10, 0, 0, 0),
                Tag = asset
            };
            btn.Click += Btn_DownloadAsset;
            Grid.SetColumn(btn, 1);

            grid.Children.Add(info);
            grid.Children.Add(btn);
            border.Child = grid;
            return border;
        }

        /// <summary>
        /// Downloads a selected GitHub asset (ZIP only), extracts it,
        /// and auto-configures the frpc path to the downloaded executable.
        /// </summary>
        async void Btn_DownloadAsset(object s, RoutedEventArgs e)
        {
            if (s is not Button b || b.Tag is not GitHubAsset asset) return;
            if (!asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(L("S_ZipOnly"), L("S_ZipOnlyTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string version = DownloadHelper.ParseVersion(asset.name);
            string savePath = Path.Combine(DownloadHelper.DownloadDir, asset.name);
            Directory.CreateDirectory(DownloadHelper.DownloadDir);

            // Cancel any in-progress download
            _dlCts?.Cancel();
            _dlCts = new CancellationTokenSource();

            // Show progress UI
            ProgressPanel.Visibility = Visibility.Visible;
            TxtDlFile.Text = $"{L("S_DownloadProgress")}{asset.name}";
            DlProgress.Value = 0;

            try
            {
                // ── Download with progress reporting ──
                var prog = new Progress<double>(pct =>
                {
                    DlProgress.Value = pct;
                    TxtDlPct.Text = $"{pct:F1}%";
                });
                await GithubHelper.DownloadAsync(
                    asset.browser_download_url, savePath, prog, _dlCts.Token);

                // ── Extract the ZIP ──
                TxtDlFile.Text = $"{L("S_ExtractProgress")}{version} ...";
                DlProgress.Value = 100;

                string extractedDir = await Task.Run(
                    () => DownloadHelper.ExtractAndCleanup(savePath, version));

                TxtDlFile.Text = $"{L("S_ExtractDone")}frp-{version}/";
                SetStatus(L("S_ExtractAndDone") + version);

                // ── Auto-set frpc path to the extracted executable ──
                string frpcExe = Path.Combine(extractedDir, "frpc.exe");
                if (File.Exists(frpcExe))
                {
                    SetFrpcPath(frpcExe);
                    _term?.Append(L("S_TermAutoPath") + frpcExe + " ───", TerminalWriter.BrushSuccess);
                }
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                ProgressPanel.Visibility = Visibility.Collapsed;
                SetStatus(L("S_DownloadCancelled"));
            }
            catch (Exception ex)
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                ProgressPanel.Visibility = Visibility.Collapsed;
                MessageBox.Show(L("S_MsgDownloadFail") + ex.Message,
                    L("S_MsgDownloadFailTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Opens the FRP GitHub releases page in the default browser.</summary>
        void Btn_OpenGithub(object s, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo(
                "https://github.com/fatedier/frp/releases")
            { UseShellExecute = true });

        // ══ Helpers ═════════════════════════════════════════════════════════

        /// <summary>Creates a centered, wrapped TextBlock for info/error messages.</summary>
        static TextBlock MakeTextBlock(string text, Brush? fg = null) => new()
        {
            Text = text,
            Foreground = fg ?? new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        /// <summary>Refreshes a ListBox's items while preserving the selected index.</summary>
        static void RefreshList(ListBox lb)
        {
            int idx = lb.SelectedIndex;
            lb.Items.Refresh();
            lb.SelectedIndex = idx;
        }

        /// <summary>Updates the status bar text.</summary>
        void SetStatus(string msg) => TxtStatus.Text = msg;

        /// <summary>Updates proxy and visitor count labels in the sidebar and status bar.</summary>
        void UpdateCounts()
        {
            TxtProxyCount.Text = $"({_proxies.Count})";
            TxtVisitorCount.Text = $"({_visitors.Count})";
            TxtCounts.Text = $"{_proxies.Count}{L("S_ProxyCountFmt")}  " +
                             $"{_visitors.Count}{L("S_VisitorCountFmt")}";
        }

        // ══ Window close → minimize to tray ═════════════════════════════════

        /// <summary>
        /// Overrides window closing to minimize to tray instead of closing.
        /// Shows a balloon tip to inform the user the app is still running.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_tray != null)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                _tray.HideToTray();

                // Show balloon tip on first minimize
                _tray.ShowBalloon(L("S_TrayBalloonTitle"), L("S_TrayBalloonText"));
            }
            base.OnClosing(e);
        }

        /// <summary>
        /// Disposes the frpc process manager when the window is fully closed.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _frpc?.Dispose();
            base.OnClosed(e);
        }
    }
}
