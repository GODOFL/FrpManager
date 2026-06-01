using FrpManager.Helpers;
using FrpManager.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Tomlyn;
using Tomlyn.Model;

namespace FrpManager.Views
{
    public partial class MainWindow : Window
    {
        // ── State ─────────────────────────────────────────────────────────
        private readonly ServerConfig _server = new();
        private readonly ObservableCollection<ProxyConfig> _proxies = new();
        private readonly ObservableCollection<VisitorConfig> _visitors = new();
        private AppSettings _settings = new();

        private ProxyConfig? _curProxy;
        private VisitorConfig? _curVisitor;
        private bool _busy;
        private bool _isEnglish = false;

        private Process? _frpProc;
        private CancellationTokenSource? _dlCts;

        // ── Terminal colours ──────────────────────────────────────────────
        private static readonly Brush BrushInfo = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE));
        private static readonly Brush BrushWarn = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60));
        private static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        private static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0xA0));
        private static readonly Brush BrushMuted = new SolidColorBrush(Color.FromRgb(0x60, 0x88, 0xA0));

        // ── L() — localisation helper ─────────────────────────────────────
        string L(string key) => TryFindResource(key) as string ?? key;

        // ══ Constructor ═══════════════════════════════════════════════════

        public MainWindow()
        {
            InitializeComponent();
            ProxyList.ItemsSource = _proxies;
            VisitorList.ItemsSource = _visitors;
            _settings = SettingsHelper.Load();

            // Restore saved language
            if (_settings.Language == "en-US")
            {
                _isEnglish = true;
                var uri = new Uri("Localization/Strings.en-US.xaml", UriKind.Relative);
                var dicts = Application.Current.Resources.MergedDictionaries;
                var existing = dicts.FirstOrDefault(d =>
                    d.Source?.OriginalString.Contains("Localization") == true);
                if (existing != null) dicts.Remove(existing);
                dicts.Add(new ResourceDictionary { Source = uri });
            }

            LoadServerToUI();
            LoadFrpcPathsToUI();
            RefreshPreview();
            SetStatus(L("S_Ready"));

            MainTabs.SelectionChanged += (s, e) =>
            {
                if (MainTabs.SelectedItem == TabTomlLib)
                    LoadTomlFileList();
            };
        }

        // ══ Language toggle ═══════════════════════════════════════════════

        void Btn_ToggleLang(object s, RoutedEventArgs e)
        {
            _isEnglish = !_isEnglish;
            var lang = _isEnglish ? "en-US" : "zh-CN";
            var uri = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative);

            var dicts = Application.Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Localization") == true);
            if (existing != null) dicts.Remove(existing);
            dicts.Add(new ResourceDictionary { Source = uri });

            _settings.Language = lang;
            SettingsHelper.Save(_settings);

            UpdateCounts();

            // Refresh code-driven status labels
            bool running = _frpProc != null && !_frpProc.HasExited;
            TxtFrpStatus.Text = running ? L("S_FrpcRunning") : L("S_FrpcNotRunning");
            TxtTermStatus.Text = running
                ? $"{L("S_FrpcRunning")} (PID {_frpProc?.Id})"
                : L("S_FrpcNotRunning");
            TxtStartLabel.Text = running ? L("S_StopFrpc") : L("S_StartFrpc");
            TxtStatus.Text = L("S_Ready");
            UpdateFrpcHint();
        }

        // ══ FRP Path ══════════════════════════════════════════════════════

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

        void FrpcPath_Changed(object s, SelectionChangedEventArgs e)
        {
            if (_busy) return;
            _settings.FrpcPath = CmbFrpcPath.Text;
            SettingsHelper.Save(_settings);
            UpdateFrpcHint();
        }

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

        void Btn_BrowseFrpc(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog
            {
                Title = L("S_FrpcPathSection"),
                Filter = "frpc|frpc.exe;frpc|All files|*.*"
            };
            if (d.ShowDialog() == true) SetFrpcPath(d.FileName);
        }

        void Btn_ScanFrpc(object s, RoutedEventArgs e)
        {
            SetStatus(L("S_StatusScanning"));
            var latest = DownloadHelper.FindLatestFrpc();
            if (latest != null)
            {
                SetFrpcPath(latest);
                SetStatus(L("S_StatusScanLatest") +
                    Path.GetFileName(Path.GetDirectoryName(latest)!));
                return;
            }
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

        void SetFrpcPath(string path)
        {
            _settings.FrpcPath = path;
            if (!_settings.RecentFrpcPaths.Contains(path))
                _settings.RecentFrpcPaths.Insert(0, path);
            if (_settings.RecentFrpcPaths.Count > 8)
                _settings.RecentFrpcPaths = _settings.RecentFrpcPaths.Take(8).ToList();
            SettingsHelper.Save(_settings);
            _busy = true;
            CmbFrpcPath.Text = path;
            _busy = false;
            UpdateFrpcHint();
        }

        // ══ Server UI ═════════════════════════════════════════════════════

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

        void Server_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            if (S_Addr == null || S_Port == null || S_Token == null || S_LogFile == null || S_LogLevel == null)
                return;

            _server.ServerAddr = S_Addr.Text.Trim();
            _server.NatHoleStunServer = S_StunServer.Text.Trim();
            if (int.TryParse(S_Port.Text, out int p)) _server.ServerPort = p;
            _server.Token = S_Token.Text;
            _server.LogFile = S_LogFile.Text;
            if (S_LogLevel.SelectedItem is ComboBoxItem li)
                _server.LogLevel = li.Content?.ToString() ?? "info";
        }

        void Server_AuthChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy) return;
            if (S_AuthMethod.SelectedItem is not ComboBoxItem ci) return;
            var raw = ci.Content?.ToString() ?? "";
            var method = raw.Contains("none") || raw.Contains("不认证") || raw == "none (no auth)"
                ? "none"
                : raw.Contains("oidc") ? "oidc" : "token";
            _server.AuthMethod = method;
            if (TokenPanel != null)
                TokenPanel.Visibility = method == "token"
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        void Server_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            _server.TlsEnable = S_Tls.IsChecked == true;
        }

        void Btn_BrowseLog(object s, RoutedEventArgs e)
        {
            var d = new SaveFileDialog { Filter = "Log|*.log|All|*.*", FileName = "frpc.log" };
            if (d.ShowDialog() == true) S_LogFile.Text = d.FileName;
        }

        // ══ Proxy CRUD ════════════════════════════════════════════════════

        void Btn_AddProxy(object s, RoutedEventArgs e)
        {
            var p = new ProxyConfig { Name = $"proxy-{_proxies.Count + 1}" };
            _proxies.Add(p);
            VisitorList.SelectedItem = null;
            ProxyList.SelectedItem = p;
            UpdateCounts();
        }

        void Btn_DeleteProxy(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is ProxyConfig p)
            {
                _proxies.Remove(p);
                if (_curProxy == p) { _curProxy = null; ShowEditor(EditorMode.None); }
                UpdateCounts();
                SetStatus(L("S_DeletedProxy") + p.Name);
            }
        }

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
            foreach (ComboBoxItem item in F_Type.Items)
                if (item.Content?.ToString() == p.Type.ToString())
                { F_Type.SelectedItem = item; break; }
            _busy = false;
            ApplyProxyTypeVisibility(p.Type);
        }

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

        void Proxy_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curProxy == null) return;
            _curProxy.UseEncryption = F_Encrypt.IsChecked == true;
            _curProxy.UseCompression = F_Compress.IsChecked == true;
        }

        void ApplyProxyTypeVisibility(ProxyType t)
        {
            bool isHttp = t is ProxyType.http or ProxyType.https;
            bool isSecret = t is ProxyType.stcp or ProxyType.xtcp or ProxyType.sudp;
            CardRemote.Visibility = (!isHttp && !isSecret) ? Visibility.Visible : Visibility.Collapsed;
            CardHttp.Visibility = isHttp ? Visibility.Visible : Visibility.Collapsed;
            CardSecret.Visibility = isSecret ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══ Visitor CRUD ══════════════════════════════════════════════════

        void Btn_AddVisitor(object s, RoutedEventArgs e)
        {
            var v = new VisitorConfig { Name = $"visitor-{_visitors.Count + 1}" };
            _visitors.Add(v);
            ProxyList.SelectedItem = null;
            VisitorList.SelectedItem = v;
            UpdateCounts();
        }

        void Btn_DeleteVisitor(object s, RoutedEventArgs e)
        {
            if (s is Button b && b.Tag is VisitorConfig v)
            {
                _visitors.Remove(v);
                if (_curVisitor == v) { _curVisitor = null; ShowEditor(EditorMode.None); }
                UpdateCounts();
                SetStatus(L("S_DeletedVisitor") + v.Name);
            }
        }

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
            foreach (ComboBoxItem item in V_Type.Items)
                if (item.Content?.ToString() == v.Type.ToString())
                { V_Type.SelectedItem = item; break; }
            _busy = false;
            ApplyVisitorTypeVisibility(v.Type);
        }

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

        void Visitor_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            _curVisitor.KeepTunnelOpen = V_KeepTunnelOpen.IsChecked == true;
        }

        void ApplyVisitorTypeVisibility(ProxyType t)
        {
            CardXtcp.Visibility = t == ProxyType.xtcp
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ══ Editor visibility ══════════════════════════════════════════════

        enum EditorMode { None, Proxy, Visitor }

        void ShowEditor(EditorMode mode)
        {
            EmptyState.Visibility = mode == EditorMode.None ? Visibility.Visible : Visibility.Collapsed;
            ProxyEditorPanel.Visibility = mode == EditorMode.Proxy ? Visibility.Visible : Visibility.Collapsed;
            VisitorEditorPanel.Visibility = mode == EditorMode.Visitor ? Visibility.Visible : Visibility.Collapsed;
            if (mode != EditorMode.None && MainTabs.SelectedItem != TabEditor)
                MainTabs.SelectedItem = TabEditor;
        }

        // ══ Templates ═════════════════════════════════════════════════════

        void AddProxy(ProxyConfig t)
        {
            _proxies.Add(t);
            VisitorList.SelectedItem = null;
            ProxyList.SelectedItem = t;
            UpdateCounts();
            SetStatus(L("S_AddedTemplate") + t.Name);
        }

        void AddVisitor(VisitorConfig v)
        {
            _visitors.Add(v);
            ProxyList.SelectedItem = null;
            VisitorList.SelectedItem = v;
            UpdateCounts();
            SetStatus(L("S_AddedVisitorTemplate") + v.Name);
        }

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

        // ══ Preview / Export ══════════════════════════════════════════════

        void RefreshPreview()
            => PreviewBox.Text = ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors);

        void Btn_Refresh(object s, RoutedEventArgs e) => RefreshPreview();

        void Btn_CopyAll(object s, RoutedEventArgs e)
        {
            Clipboard.SetText(PreviewBox.Text);
            SetStatus(L("S_StatusCopied"));
        }

        void Btn_ExportFrpc(object s, RoutedEventArgs e)
            => ExportToml("frpc.toml", ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

        void Btn_ExportFrps(object s, RoutedEventArgs e)
            => ExportToml("frps.toml", ConfigHelper.GenerateFrpsToml(_server));

        void ExportToml(string name, string content)
        {
            var d = new SaveFileDialog { Filter = "TOML|*.toml|All|*.*", FileName = name };
            if (d.ShowDialog() == true)
            {
                File.WriteAllText(d.FileName, content);
                SetStatus(L("S_StatusSaved") + d.FileName);
            }
        }

        void Btn_Open(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "TOML|*.toml|All|*.*" };
            if (d.ShowDialog() == true)
                SetStatus(L("S_Opened") + Path.GetFileName(d.FileName) + L("S_OpenedSuffix"));
        }

        void Btn_Save(object s, RoutedEventArgs e) => Btn_ExportFrpc(s, e);

        // ══ FRP Launch ════════════════════════════════════════════════════

        void Btn_Start(object s, RoutedEventArgs e)
        {
            if (_frpProc != null && !_frpProc.HasExited)
            {
                _frpProc.Kill(true);
                _frpProc = null;
                SetFrpRunning(false);
                AppendTerminal(L("S_TermStopped"), BrushMuted);
                SetStatus(L("S_StatusStopped"));
                return;
            }

            string frpcPath = CmbFrpcPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            {
                MessageBox.Show(L("S_MsgNoFrpcPath"), L("S_MsgNoFrpcPathTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MainTabs.SelectedItem = TabServer;
                return;
            }

            var (valid, errors) = ConfigHelper.Validate(_server, _proxies, _visitors);
            if (!valid)
            {
                MessageBox.Show(L("S_MsgValidateFail") + "\n\n" + string.Join("\n", errors),
                    L("S_MsgValidateTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string tomlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tomlset");
            Directory.CreateDirectory(tomlDir);
            string tmp = Path.Combine(tomlDir, "frpc_mgr_tmp.toml");
            File.WriteAllText(tmp, ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

            _frpProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = $"-c \"{tmp}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true
            };

            _frpProc.OutputDataReceived += (_, de) =>
            {
                if (de.Data != null)
                    Dispatcher.Invoke(() => AppendTerminalLine(de.Data));
            };
            _frpProc.ErrorDataReceived += (_, de) =>
            {
                if (de.Data != null)
                    Dispatcher.Invoke(() => AppendTerminalLine(de.Data, isStderr: true));
            };
            _frpProc.Exited += (_, _) =>
                Dispatcher.Invoke(() =>
                {
                    SetFrpRunning(false);
                    AppendTerminal(
                        L("S_TermExited") + (_frpProc?.ExitCode.ToString() ?? "?") + " )───",
                        BrushMuted);
                    SetStatus(L("S_StatusExited"));
                });

            _frpProc.Start();
            _frpProc.BeginOutputReadLine();
            _frpProc.BeginErrorReadLine();

            SetFrpRunning(true);
            SetStatus(L("S_StatusStarted") + _frpProc.Id + ")");
            AppendTerminal(L("S_TermStarted") + frpcPath + " ───", BrushSuccess);
            AppendTerminal(L("S_TermConfig") + tmp + " ───", BrushMuted);
            MainTabs.SelectedItem = TabTerminal;
        }

        void SetFrpRunning(bool on)
        {
            var green = Color.FromRgb(0x52, 0xB7, 0x88);
            StatusDot.Fill = new SolidColorBrush(on ? green : Color.FromRgb(0xC0, 0xC0, 0xC0));
            TermDot.Fill = new SolidColorBrush(on ? green : Color.FromRgb(0x55, 0x55, 0x55));
            TxtFrpStatus.Text = on ? L("S_FrpcRunning") : L("S_FrpcNotRunning");
            TxtTermStatus.Text = on
                ? $"{L("S_FrpcRunning")} (PID {_frpProc?.Id})"
                : L("S_FrpcNotRunning");
            TxtStartIcon.Text = on ? "⏹" : "▶";
            TxtStartLabel.Text = on ? L("S_StopFrpc") : L("S_StartFrpc");
            BtnStart.Style = on
                ? (Style)FindResource("DangerBtn")
                : (Style)FindResource("GreenBtn");
        }

        // ══ Terminal ══════════════════════════════════════════════════════

        void AppendTerminalLine(string line, bool isStderr = false)
        {
            line = Regex.Replace(line, @"\x1B\[[0-9;]*m", "");
            Brush brush;
            if (isStderr) brush = BrushError;
            else if (line.Contains("[E]")) brush = BrushError;
            else if (line.Contains("[W]")) brush = BrushWarn;
            else if (line.Contains("[I]")) brush = BrushInfo;
            else if (line.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("started", StringComparison.OrdinalIgnoreCase))
                brush = BrushSuccess;
            else brush = BrushInfo;
            AppendTerminal(line, brush);
        }

        void AppendTerminal(string text, Brush brush)
        {
            var para = new Paragraph(new Run(text)) { Foreground = brush };
            TerminalBox.Document.Blocks.Add(para);
            TerminalBox.ScrollToEnd();
            while (TerminalBox.Document.Blocks.Count > 2000)
                TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);
        }

        void Btn_ClearTerminal(object s, RoutedEventArgs e)
            => TerminalBox.Document.Blocks.Clear();

        // ══ Config Library ════════════════════════════════════════════════

        void Btn_RefreshTomlList(object s, RoutedEventArgs e) => LoadTomlFileList();

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

            var btnLoad = new Button
            {
                Content = L("S_Load"),
                Style = (Style)FindResource("PrimaryBtn"),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(10, 0, 0, 0),
                Tag = path
            };
            btnLoad.Click += (_, _) => LoadTomlFile((string)btnLoad.Tag);
            Grid.SetColumn(btnLoad, 1);

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

        void LoadTomlFile(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                TomlTable model;
                try { model = Toml.ToModel(text); }
                catch
                {
                    MessageBox.Show(L("S_MsgTomlError"), L("S_MsgTomlErrorTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _server.ServerAddr = model.TryGet("serverAddr") ?? _server.ServerAddr;
                if (int.TryParse(model.TryGet("serverPort"), out int p)) _server.ServerPort = p;
                _server.NatHoleStunServer = model.TryGet("natHoleStunServer") ?? "";

                if (model.TryGetValue("auth", out var authObj) && authObj is TomlTable auth)
                {
                    _server.AuthMethod = auth.TryGet("method") ?? "none";
                    _server.Token = auth.TryGet("token") ?? "";
                }

                _proxies.Clear();
                if (model.TryGetValue("proxies", out var po) && po is TomlTableArray proxies)
                    foreach (TomlTable row in proxies)
                        _proxies.Add(new ProxyConfig
                        {
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

                _visitors.Clear();
                if (model.TryGetValue("visitors", out var vo) && vo is TomlTableArray visitors)
                    foreach (TomlTable row in visitors)
                        _visitors.Add(new VisitorConfig
                        {
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

        // ══ GitHub Download ═══════════════════════════════════════════════

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

            _dlCts?.Cancel();
            _dlCts = new CancellationTokenSource();
            ProgressPanel.Visibility = Visibility.Visible;
            TxtDlFile.Text = $"{L("S_DownloadProgress")}{asset.name}";
            DlProgress.Value = 0;

            try
            {
                var prog = new Progress<double>(pct =>
                {
                    DlProgress.Value = pct;
                    TxtDlPct.Text = $"{pct:F1}%";
                });
                await GithubHelper.DownloadAsync(
                    asset.browser_download_url, savePath, prog, _dlCts.Token);

                TxtDlFile.Text = $"{L("S_ExtractProgress")}{version} ...";
                DlProgress.Value = 100;

                string extractedDir = await Task.Run(
                    () => DownloadHelper.ExtractAndCleanup(savePath, version));

                TxtDlFile.Text = $"{L("S_ExtractDone")}frp-{version}/";
                SetStatus(L("S_ExtractAndDone") + version);

                string frpcExe = Path.Combine(extractedDir, "frpc.exe");
                if (File.Exists(frpcExe))
                {
                    SetFrpcPath(frpcExe);
                    AppendTerminal(L("S_TermAutoPath") + frpcExe + " ───", BrushSuccess);
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

        void Btn_OpenGithub(object s, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo(
                "https://github.com/fatedier/frp/releases")
            { UseShellExecute = true });

        // ══ Helpers ═══════════════════════════════════════════════════════

        static TextBlock MakeTextBlock(string text, Brush? fg = null) => new()
        {
            Text = text,
            Foreground = fg ?? new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
            FontSize = 13,
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        static void RefreshList(ListBox lb)
        {
            int idx = lb.SelectedIndex;
            lb.Items.Refresh();
            lb.SelectedIndex = idx;
        }

        void SetStatus(string msg) => TxtStatus.Text = msg;

        void UpdateCounts()
        {
            TxtProxyCount.Text = $"({_proxies.Count})";
            TxtVisitorCount.Text = $"({_visitors.Count})";
            TxtCounts.Text = $"{_proxies.Count}{L("S_ProxyCountFmt")}  " +
                             $"{_visitors.Count}{L("S_VisitorCountFmt")}";
        }

        protected override void OnClosed(EventArgs e)
        {
            _frpProc?.Kill(true);
            base.OnClosed(e);
        }
    }
}