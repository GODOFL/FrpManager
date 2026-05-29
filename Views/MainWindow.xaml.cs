using FrpManager.Helpers;
using FrpManager.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
        private readonly ServerConfig                      _server   = new();
        private readonly ObservableCollection<ProxyConfig>   _proxies  = new();
        private readonly ObservableCollection<VisitorConfig> _visitors = new();
        private AppSettings _settings = new();

        private ProxyConfig?   _curProxy;
        private VisitorConfig? _curVisitor;
        private bool           _busy;

        private Process? _frpProc;
        private CancellationTokenSource? _dlCts;

        // ── Colour scheme for terminal output ─────────────────────────────
        private static readonly Brush BrushInfo    = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE));
        private static readonly Brush BrushWarn    = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60));
        private static readonly Brush BrushError   = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        private static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0xA0));
        private static readonly Brush BrushMuted   = new SolidColorBrush(Color.FromRgb(0x60, 0x88, 0xA0));

        public MainWindow()
        {
            InitializeComponent();

            MainTabs.SelectionChanged += (s, e) =>
            {
                // 配置文件库是第4个Tab（index从0算）
                if (MainTabs.SelectedItem == TabTomlLib)
                    LoadTomlFileList();
            };

            ProxyList.ItemsSource   = _proxies;
            VisitorList.ItemsSource = _visitors;
            _settings = SettingsHelper.Load();
            LoadServerToUI();
            LoadFrpcPathsToUI();
            RefreshPreview();
            SetStatus("就绪");
        }

        // ══ 配置文件库 ════════════════════════════════════════════════════════

        void Btn_RefreshTomlList(object s, RoutedEventArgs e) => LoadTomlFileList();

        void LoadTomlFileList()
        {
            string tomlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tomlset");
            TxtTomlDir.Text = tomlDir;
            TomlFilePanel.Children.Clear();

            if (!Directory.Exists(tomlDir))
            {
                TomlFilePanel.Children.Add(TxtTomlHint);
                TxtTomlHint.Text = "tomlset 文件夹不存在，启动一次 frpc 后会自动创建";
                return;
            }

            var files = Directory.GetFiles(tomlDir, "*.toml")
                                 .OrderByDescending(File.GetLastWriteTime)
                                 .ToList();

            if (files.Count == 0)
            {
                TxtTomlHint.Text = "tomlset 文件夹为空，启动 frpc 后会自动生成配置文件";
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

            // 文件信息
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
                Text = $"修改时间：{modified}    大小：{size}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(infoPanel, 0);

            // 加载按钮
            var btnLoad = new Button
            {
                Content = "📂 加载",
                Style = (Style)FindResource("PrimaryBtn"),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(10, 0, 0, 0),
                Tag = path
            };
            btnLoad.Click += (s, e) => LoadTomlFile((string)((Button)s).Tag);
            Grid.SetColumn(btnLoad, 1);

            // 删除按钮
            var btnDel = new Button
            {
                Content = "🗑 删除",
                Style = (Style)FindResource("DangerBtn"),
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = path
            };
            btnDel.Click += (s, e) =>
            {
                var p = (string)((Button)s).Tag;
                if (MessageBox.Show($"确认删除？\n{Path.GetFileName(p)}",
                        "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    == MessageBoxResult.Yes)
                {
                    File.Delete(p);
                    LoadTomlFileList();
                    SetStatus($"已删除：{Path.GetFileName(p)}");
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
            // 复用已有的导入逻辑，直接调用 Btn_Open 的解析部分
            // 把 path 传入解析流程即可
            try
            {
                var text = File.ReadAllText(path);
                var doc = Tomlyn.Toml.Parse(text);
                if (doc.HasErrors)
                {
                    MessageBox.Show("TOML 格式错误，无法加载", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var model = doc.ToModel();

                _server.ServerAddr = model.TryGet("serverAddr") ?? _server.ServerAddr;
                if (int.TryParse(model.TryGet("serverPort"), out int p)) _server.ServerPort = p;

                if (model.TryGetValue("auth", out var authObj) && authObj is TomlTable auth)
                {
                    _server.AuthMethod = auth.TryGet("method") ?? "none";
                    _server.Token = auth.TryGet("token") ?? "";
                }

                _proxies.Clear();
                if (model.TryGetValue("proxies", out var proxiesObj) && proxiesObj is TomlTableArray proxies)
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
                if (model.TryGetValue("visitors", out var visitorsObj) && visitorsObj is TomlTableArray visitors)
                    foreach (TomlTable row in visitors)
                        _visitors.Add(new VisitorConfig
                        {
                            Name = row.TryGet("name") ?? "",
                            ServerName = row.TryGet("serverName") ?? "",
                            ServerUser = row.TryGet("serverUser") ?? "",
                            Sk = row.TryGet("secretKey") ?? "",
                            BindAddr = row.TryGet("bindAddr") ?? "127.0.0.1",
                            BindPort = int.TryParse(row.TryGet("bindPort"), out int bp) ? bp : 9000,
                            Type = Enum.TryParse<ProxyType>(row.TryGet("type"), out var vt)
                                         ? vt : ProxyType.stcp,
                        });

                LoadServerToUI();
                UpdateCounts();
                RefreshPreview();
                ShowEditor(EditorMode.None);
                SetStatus($"已加载：{Path.GetFileName(path)}");

                // 切换回编辑器 Tab
                MainTabs.SelectedItem = TabEditor;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败：\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                TxtFrpcPathHint.Text = "尚未设置路径，请浏览选择或点击自动扫描";
                TxtFrpcPathHint.Foreground = (Brush)FindResource("TextMutedBrush");
            }
            else if (File.Exists(path))
            {
                TxtFrpcPathHint.Text = "✅ 路径有效";
                TxtFrpcPathHint.Foreground = (Brush)FindResource("AccentGreenBrush");
            }
            else
            {
                TxtFrpcPathHint.Text = "⚠ 文件不存在，请重新选择";
                TxtFrpcPathHint.Foreground = (Brush)FindResource("AccentRedBrush");
            }
        }

        void Btn_BrowseFrpc(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog
            {
                Title  = "选择 frpc 可执行文件",
                Filter = "frpc 程序|frpc.exe;frpc|所有文件|*.*"
            };
            if (d.ShowDialog() != true) return;
            SetFrpcPath(d.FileName);
        }

        void Btn_ScanFrpc(object s, RoutedEventArgs e)
        {
            SetStatus("正在扫描 frpc...");

            // 优先检查 download 目录里的最新版本
            var latest = DownloadHelper.FindLatestFrpc();
            if (latest != null)
            {
                SetFrpcPath(latest);
                SetStatus($"已自动读取最新版本：{Path.GetFileName(Path.GetDirectoryName(latest)!)}");
                return;
            }

            // download 目录没有，再扫描系统路径
            var found = SettingsHelper.ScanForFrpc();
            if (found.Count == 0)
            {
                SetStatus("未找到 frpc，请手动浏览指定路径");
                MessageBox.Show(
                    "自动扫描未找到 frpc 可执行文件。\n\n" +
                    "可点击「浏览...」手动选择，或在「下载 FRP」标签页下载最新版本。",
                    "扫描结果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _busy = true;
            CmbFrpcPath.Items.Clear();
            foreach (var p in found) CmbFrpcPath.Items.Add(p);
            _busy = false;

            SetFrpcPath(found[0]);
            SetStatus($"扫描完成，找到 {found.Count} 个 frpc");
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
            S_Addr.Text  = _server.ServerAddr;
            S_Port.Text  = _server.ServerPort.ToString();
            S_Token.Text = _server.Token;
            S_LogFile.Text = _server.LogFile;
            _busy = false;
        }

        void Server_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            if (S_Addr == null || S_Token == null || S_LogFile == null || S_LogLevel == null)
                return;

            _server.ServerAddr = S_Addr.Text.Trim();
            if (int.TryParse(S_Port.Text, out int p)) _server.ServerPort = p;
            _server.Token   = S_Token.Text;
            _server.LogFile = S_LogFile.Text;
            if (S_LogLevel.SelectedItem is ComboBoxItem li)
                _server.LogLevel = li.Content?.ToString() ?? "info";
        }

        // 认证方式切换：控制 Token 输入框的显示与隐藏
        void Server_AuthChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy) return;
            if (S_AuthMethod.SelectedItem is not ComboBoxItem ci) return;
            // 取出实际方法值（"none（不认证）" → "none"）
            var raw = ci.Content?.ToString() ?? "none";
            var method = raw.Contains("none") ? "none"
                       : raw.Contains("oidc")  ? "oidc"
                       : "token";
            _server.AuthMethod = method;
            // 只有 token 方式才显示 Token 输入框
            if (TokenPanel != null)
                TokenPanel.Visibility = method == "token"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        void Server_CheckChanged(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            _server.TlsEnable = S_Tls.IsChecked == true;
        }

        void Btn_BrowseLog(object s, RoutedEventArgs e)
        {
            var d = new SaveFileDialog { Filter = "日志文件|*.log|所有文件|*.*", FileName = "frpc.log" };
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
                SetStatus($"已删除代理：{p.Name}");
            }
        }

        void ProxyList_Changed(object s, SelectionChangedEventArgs e)
        {
            if (ProxyList.SelectedItem is not ProxyConfig p) return;
            _curProxy   = p;
            _curVisitor = null;
            VisitorList.SelectedItem = null;
            LoadProxyToUI(p);
            ShowEditor(EditorMode.Proxy);
            TxtProxyTitle.Text = $"编辑：{p.Name}";
        }

        void LoadProxyToUI(ProxyConfig p)
        {
            _busy = true;
            F_Name.Text       = p.Name;
            F_LocalIp.Text    = p.LocalIp;
            F_LocalPort.Text  = p.LocalPort.ToString();
            F_RemotePort.Text = p.RemotePort.ToString();
            F_Domains.Text    = p.CustomDomains;
            F_Subdomain.Text  = p.Subdomain;
            F_Sk.Text         = p.Sk;
            F_Encrypt.IsChecked  = p.UseEncryption;
            F_Compress.IsChecked = p.UseCompression;
            foreach (ComboBoxItem item in F_Type.Items)
                if (item.Content?.ToString() == p.Type.ToString())
                { F_Type.SelectedItem = item; break; }
            ApplyProxyTypeVisibility(p.Type);
            _busy = false;
        }

        void Proxy_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curProxy == null) return;
            _curProxy.Name          = F_Name.Text;
            _curProxy.LocalIp       = F_LocalIp.Text;
            _curProxy.CustomDomains = F_Domains.Text;
            _curProxy.Subdomain     = F_Subdomain.Text;
            _curProxy.Sk            = F_Sk.Text;
            if (int.TryParse(F_LocalPort.Text,  out int lp)) _curProxy.LocalPort  = lp;
            if (int.TryParse(F_RemotePort.Text, out int rp)) _curProxy.RemotePort = rp;
            TxtProxyTitle.Text = $"编辑：{_curProxy.Name}";
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
            _curProxy.UseEncryption  = F_Encrypt.IsChecked == true;
            _curProxy.UseCompression = F_Compress.IsChecked == true;
        }

        void ApplyProxyTypeVisibility(ProxyType t)
        {
            bool isHttp   = t is ProxyType.http  or ProxyType.https;
            bool isSecret = t is ProxyType.stcp  or ProxyType.xtcp;
            CardRemote.Visibility = (!isHttp && !isSecret) ? Visibility.Visible : Visibility.Collapsed;
            CardHttp.Visibility   = isHttp   ? Visibility.Visible : Visibility.Collapsed;
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
                SetStatus($"已删除访客：{v.Name}");
            }
        }

        void VisitorList_Changed(object s, SelectionChangedEventArgs e)
        {
            if (VisitorList.SelectedItem is not VisitorConfig v) return;
            _curVisitor = v;
            _curProxy   = null;
            ProxyList.SelectedItem = null;
            LoadVisitorToUI(v);
            ShowEditor(EditorMode.Visitor);
            TxtVisitorTitle.Text = $"编辑：{v.Name}";
        }

        void LoadVisitorToUI(VisitorConfig v)
        {
            _busy = true;
            V_Name.Text       = v.Name;
            V_ServerName.Text = v.ServerName;
            V_ServerUser.Text = v.ServerUser;
            V_Sk.Text         = v.Sk;
            V_BindAddr.Text   = v.BindAddr;
            V_BindPort.Text   = v.BindPort.ToString();
            foreach (ComboBoxItem item in V_Type.Items)
                if (item.Content?.ToString() == v.Type.ToString())
                { V_Type.SelectedItem = item; break; }
            _busy = false;
        }

        void Visitor_FieldChanged(object s, RoutedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            _curVisitor.Name       = V_Name.Text;
            _curVisitor.ServerName = V_ServerName.Text;
            _curVisitor.ServerUser = V_ServerUser.Text;
            _curVisitor.Sk         = V_Sk.Text;
            _curVisitor.BindAddr   = V_BindAddr.Text;
            if (int.TryParse(V_BindPort.Text, out int bp)) _curVisitor.BindPort = bp;
            TxtVisitorTitle.Text = $"编辑：{_curVisitor.Name}";
            RefreshList(VisitorList);
        }

        void Visitor_TypeChanged(object s, SelectionChangedEventArgs e)
        {
            if (_busy || _curVisitor == null) return;
            if (V_Type.SelectedItem is ComboBoxItem ci &&
                Enum.TryParse<ProxyType>(ci.Content?.ToString(), out var t))
            {
                _curVisitor.Type = t;
                RefreshList(VisitorList);
            }
        }

        // ══ Editor visibility ══════════════════════════════════════════════

        enum EditorMode { None, Proxy, Visitor }

        void ShowEditor(EditorMode mode)
        {
            EmptyState.Visibility        = mode == EditorMode.None    ? Visibility.Visible : Visibility.Collapsed;
            ProxyEditorPanel.Visibility  = mode == EditorMode.Proxy   ? Visibility.Visible : Visibility.Collapsed;
            VisitorEditorPanel.Visibility= mode == EditorMode.Visitor  ? Visibility.Visible : Visibility.Collapsed;
            // Switch to editor tab
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
            SetStatus($"已添加模板：{t.Name}");
        }

        void AddVisitor(VisitorConfig v)
        {
            _visitors.Add(v);
            ProxyList.SelectedItem = null;
            VisitorList.SelectedItem = v;
            UpdateCounts();
            SetStatus($"已添加访客模板：{v.Name}");
        }

        void T_Tcp(object s, RoutedEventArgs e)     => AddProxy(ConfigHelper.TcpTemplate());
        void T_Ssh(object s, RoutedEventArgs e)     => AddProxy(ConfigHelper.SshTemplate());
        void T_Rdp(object s, RoutedEventArgs e)     => AddProxy(ConfigHelper.RdpTemplate());
        void T_Web(object s, RoutedEventArgs e)     => AddProxy(ConfigHelper.WebTemplate());
        void T_Https(object s, RoutedEventArgs e)   => AddProxy(ConfigHelper.HttpsTemplate());
        void T_Udp(object s, RoutedEventArgs e)     => AddProxy(ConfigHelper.UdpTemplate());
        void T_Mc(object s, RoutedEventArgs e)      => AddProxy(ConfigHelper.McTemplate());
        void T_Stcp(object s, RoutedEventArgs e)    => AddProxy(ConfigHelper.StcpTemplate());
        void T_Visitor(object s, RoutedEventArgs e) => AddVisitor(ConfigHelper.VisitorTemplate());

        // ══ Preview / Export ══════════════════════════════════════════════

        void RefreshPreview()
            => PreviewBox.Text = ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors);

        void Btn_Refresh(object s, RoutedEventArgs e)  => RefreshPreview();

        void Btn_CopyAll(object s, RoutedEventArgs e)
        {
            Clipboard.SetText(PreviewBox.Text);
            SetStatus("配置内容已复制到剪贴板");
        }

        void Btn_ExportFrpc(object s, RoutedEventArgs e)
            => ExportToml("frpc.toml", ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

        void Btn_ExportFrps(object s, RoutedEventArgs e)
            => ExportToml("frps.toml", ConfigHelper.GenerateFrpsToml(_server));

        void ExportToml(string name, string content)
        {
            var d = new SaveFileDialog { Filter = "TOML 配置文件|*.toml|所有文件|*.*", FileName = name };
            if (d.ShowDialog() == true) { File.WriteAllText(d.FileName, content); SetStatus($"已导出：{d.FileName}"); }
        }

        void Btn_Open(object s, RoutedEventArgs e)
        {
            var d = new OpenFileDialog { Filter = "TOML 配置文件|*.toml|所有文件|*.*" };
            if (d.ShowDialog() != true) return;

            var text = File.ReadAllText(d.FileName);
            var doc = Tomlyn.Toml.Parse(text);
            if (doc.HasErrors) { MessageBox.Show("TOML 格式错误"); return; }

            var model = doc.ToModel();

            // 读服务器
            _server.ServerAddr = model.TryGet("serverAddr");
            if (int.TryParse(model.TryGet("serverPort"), out int p)) _server.ServerPort = p;

            // 读 auth
            if (model.TryGetValue("auth", out var authObj) && authObj is TomlTable auth)
            {
                _server.AuthMethod = auth.TryGet("method") ?? "none";
                _server.Token = auth.TryGet("token") ?? "";
            }

            // 读 proxies
            _proxies.Clear();
            if (model.TryGetValue("proxies", out var proxiesObj) && proxiesObj is TomlTableArray proxies)
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

            // 读 visitors
            _visitors.Clear();
            if (model.TryGetValue("visitors", out var visitorsObj) && visitorsObj is TomlTableArray visitors)
                foreach (TomlTable row in visitors)
                    _visitors.Add(new VisitorConfig
                    {
                        Name = row.TryGet("name") ?? "",
                        ServerName = row.TryGet("serverName") ?? "",
                        ServerUser = row.TryGet("serverUser") ?? "",
                        Sk = row.TryGet("secretKey") ?? "",
                        BindAddr = row.TryGet("bindAddr") ?? "127.0.0.1",
                        BindPort = int.TryParse(row.TryGet("bindPort"), out int bp) ? bp : 9000,
                        Type = Enum.TryParse<ProxyType>(row.TryGet("type"), out var vt)
                            ? vt : ProxyType.stcp,
                    });

            LoadServerToUI();
            RefreshPreview();
            SetStatus($"已导入：{Path.GetFileName(d.FileName)}");
        }

        static string? TryGet(TomlTable t, string key)
            => t.TryGetValue(key, out var v) ? v?.ToString() : null;

        void Btn_Save(object s, RoutedEventArgs e) => Btn_ExportFrpc(s, e);

        // ══ FRP Launch ════════════════════════════════════════════════════

        void Btn_Start(object s, RoutedEventArgs e)
        {
            // Stop if already running
            if (_frpProc != null && !_frpProc.HasExited)
            {
                _frpProc.Kill(true);
                _frpProc = null;
                SetFrpRunning(false);
                AppendTerminal("─── frpc 已停止 ───", BrushMuted);
                SetStatus("frpc 已停止");
                return;
            }

            // ── 1. Resolve frpc path ──────────────────────────────────────
            string frpcPath = CmbFrpcPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            {
                MessageBox.Show(
                    "请先在「服务器配置」标签页中设置有效的 frpc 路径。\n\n" +
                    "可点击「浏览...」手动选择，或点击「🔍 自动扫描」自动查找。",
                    "未设置 frpc 路径", MessageBoxButton.OK, MessageBoxImage.Warning);
                MainTabs.SelectedItem = TabServer;
                return;
            }

            // ── 2. Validate config ────────────────────────────────────────
            var (valid, errors) = ConfigHelper.Validate(_server, _proxies, _visitors);
            if (!valid)
            {
                var msg = "配置存在以下问题，已阻止启动：\n\n" + string.Join("\n", errors);
                MessageBox.Show(msg, "配置校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ── 3. Write temp config ──────────────────────────────────────
            string tomlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tomlset");
            Directory.CreateDirectory(tomlDir); // 不存在则自动创建
            string tmp = Path.Combine(tomlDir, "frpc_mgr_tmp.toml");
            File.WriteAllText(tmp, ConfigHelper.GenerateFrpcToml(_server, _proxies, _visitors));

            // ── 4. Launch process ─────────────────────────────────────────
            _frpProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName               = frpcPath,
                    Arguments              = $"-c \"{tmp}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
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
                    AppendTerminal($"─── frpc 进程退出（代码 {_frpProc?.ExitCode}）───", BrushMuted);
                    SetStatus("frpc 进程已退出");
                });

            _frpProc.Start();
            _frpProc.BeginOutputReadLine();
            _frpProc.BeginErrorReadLine();

            SetFrpRunning(true);
            SetStatus($"frpc 已启动（PID {_frpProc.Id}）");
            AppendTerminal($"─── 启动 frpc: {frpcPath} ───", BrushSuccess);
            AppendTerminal($"─── 配置文件: {tmp} ───", BrushMuted);

            // Auto-switch to terminal tab
            MainTabs.SelectedItem = TabTerminal;
        }

        void SetFrpRunning(bool on)
        {
            var green = Color.FromRgb(0x52, 0xB7, 0x88);
            var grey  = Color.FromRgb(0x55, 0x55, 0x55);
            StatusDot.Fill     = new SolidColorBrush(on ? green : Color.FromRgb(0xC0, 0xC0, 0xC0));
            TermDot.Fill       = new SolidColorBrush(on ? green : grey);
            TxtFrpStatus.Text  = on ? "frpc 运行中" : "frpc 未运行";
            TxtTermStatus.Text = on ? $"frpc 运行中 (PID {_frpProc?.Id})" : "frpc 未运行";
            TxtStartIcon.Text  = on ? "⏹" : "▶";
            TxtStartLabel.Text = on ? "停止 frpc" : "启动 frpc";
            BtnStart.Style = on
                ? (Style)FindResource("DangerBtn")
                : (Style)FindResource("GreenBtn");
        }

        // ══ Terminal ══════════════════════════════════════════════════════

        void AppendTerminalLine(string line, bool isStderr = false)
        {
            line = Regex.Replace(line, @"\x1B\[[0-9;]*m", "");

            // Colour by frpc log level markers
            Brush brush;
            if      (isStderr)             brush = BrushError;
            else if (line.Contains("[E]")) brush = BrushError;
            else if (line.Contains("[W]")) brush = BrushWarn;
            else if (line.Contains("[I]")) brush = BrushInfo;
            else if (line.Contains("success", StringComparison.OrdinalIgnoreCase)
                  || line.Contains("started", StringComparison.OrdinalIgnoreCase))
                                           brush = BrushSuccess;
            else                           brush = BrushInfo;
            AppendTerminal(line, brush);
        }

        void AppendTerminal(string text, Brush brush)
        {
            var para = new Paragraph(new Run(text))
            {
                Foreground = brush,
                Margin = new Thickness(0),
                LineHeight = 18
            };
            TerminalBox.Document.Blocks.Add(para);
            TerminalBox.ScrollToEnd();

            // Keep at most 2000 lines to avoid memory bloat
            while (TerminalBox.Document.Blocks.Count > 2000)
                TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);
        }

        void Btn_ClearTerminal(object s, RoutedEventArgs e)
            => TerminalBox.Document.Blocks.Clear();

        // ══ GitHub Download ═══════════════════════════════════════════════

        async void Btn_CheckUpdate(object s, RoutedEventArgs e)
        {
            TxtRelInfo.Text = "正在连接 GitHub...";
            AssetPanel.Children.Clear();
            var hint = MakeTextBlock("⏳ 正在加载版本列表，请稍候...");
            AssetPanel.Children.Add(hint);
            try
            {
                var releases = await GithubHelper.GetReleasesAsync();
                AssetPanel.Children.Clear();
                foreach (var rel in releases)
                {
                    var hdr = new Border
                    {
                        Background  = new SolidColorBrush(Color.FromRgb(0xD8, 0xEE, 0xF8)),
                        CornerRadius = new CornerRadius(8),
                        Padding  = new Thickness(14, 8, 14, 8),
                        Margin   = new Thickness(0, 0, 0, 4)
                    };
                    var hSp = new StackPanel { Orientation = Orientation.Horizontal };
                    hSp.Children.Add(new TextBlock { Text = $"🏷  {rel.name ?? rel.tag_name}",
                        Foreground = (Brush)FindResource("AccentDarkBrush"),
                        FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    if (rel.published_at.Length >= 10)
                        hSp.Children.Add(new TextBlock { Text = $"    {rel.published_at[..10]}",
                            Foreground = (Brush)FindResource("TextSecondaryBrush"),
                            FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    hdr.Child = hSp;
                    AssetPanel.Children.Add(hdr);
                    foreach (var asset in rel.assets.OrderByDescending(a =>
                        a.name.Contains("windows", StringComparison.OrdinalIgnoreCase)).ThenBy(a => a.name))
                        AssetPanel.Children.Add(BuildAssetRow(asset));
                    AssetPanel.Children.Add(new Border { Height = 8 });
                }
                TxtRelInfo.Text = $"已加载 {releases.Count} 个版本，最新：{releases.FirstOrDefault()?.tag_name}";
                SetStatus("版本列表加载完成");
            }
            catch (Exception ex)
            {
                AssetPanel.Children.Clear();
                AssetPanel.Children.Add(MakeTextBlock($"❌ 加载失败：{ex.Message}",
                    (Brush)FindResource("AccentRedBrush")));
                TxtRelInfo.Text = "加载失败";
            }
        }

        UIElement BuildAssetRow(GitHubAsset asset)
        {
            bool isWin = asset.name.Contains("windows", StringComparison.OrdinalIgnoreCase);
            var border = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 2, 0, 0)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = (isWin ? "🪟 " : "") + asset.name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x50)),
                FontFamily = new FontFamily("Consolas"), FontSize = 13 });
            info.Children.Add(new TextBlock { Text = asset.SizeLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(info, 0);
            var btn = new Button { Content = "⬇  下载", Style = (Style)FindResource("PrimaryBtn"),
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(10, 0, 0, 0), Tag = asset };
            btn.Click += Btn_DownloadAsset;
            Grid.SetColumn(btn, 1);
            grid.Children.Add(info); grid.Children.Add(btn);
            border.Child = grid;
            return border;
        }

        async void Btn_DownloadAsset(object s, RoutedEventArgs e)
        {
            if (s is not Button b || b.Tag is not GitHubAsset asset) return;

            // 只处理 zip 文件
            if (!asset.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("目前仅支持自动处理 .zip 格式的压缩包。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string version = DownloadHelper.ParseVersion(asset.name);
            string savePath = Path.Combine(DownloadHelper.DownloadDir, asset.name);
            Directory.CreateDirectory(DownloadHelper.DownloadDir);

            _dlCts?.Cancel();
            _dlCts = new CancellationTokenSource();

            ProgressPanel.Visibility = Visibility.Visible;
            TxtDlFile.Text = $"下载中：{asset.name}  →  download/";
            DlProgress.Value = 0;

            try
            {
                // ── 1. 下载 ──────────────────────────────────────────────────
                var prog = new Progress<double>(pct =>
                {
                    DlProgress.Value = pct;
                    TxtDlPct.Text = $"{pct:F1}%";
                });
                await GithubHelper.DownloadAsync(
                    asset.browser_download_url, savePath, prog, _dlCts.Token);

                TxtDlFile.Text = $"解压中：frp-{version} ...";
                DlProgress.Value = 100;

                // ── 2. 解压 + 重命名 + 删除压缩包 ────────────────────────────
                string extractedDir = await Task.Run(
                    () => DownloadHelper.ExtractAndCleanup(savePath, version));

                TxtDlFile.Text = $"✅ 已完成：download/frp-{version}/";
                SetStatus($"下载并解压完成：frp-{version}");

                // ── 3. 自动设置 frpc 路径为刚下载的版本 ──────────────────────
                string frpcExe = Path.Combine(extractedDir, "frpc.exe");
                if (File.Exists(frpcExe))
                {
                    SetFrpcPath(frpcExe);
                    AppendTerminal($"─── 已自动设置 frpc 路径：{frpcExe} ───", BrushSuccess);
                }
            }
            catch (OperationCanceledException)
            {
                // 下载取消后清理未完成的文件
                if (File.Exists(savePath)) File.Delete(savePath);
                ProgressPanel.Visibility = Visibility.Collapsed;
                SetStatus("下载已取消");
            }
            catch (Exception ex)
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                ProgressPanel.Visibility = Visibility.Collapsed;
                MessageBox.Show($"下载失败：\n{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void Btn_OpenGithub(object s, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo("https://github.com/fatedier/frp/releases")
                { UseShellExecute = true });

        // ══ Helpers ═══════════════════════════════════════════════════════

        static TextBlock MakeTextBlock(string text, Brush? fg = null) => new()
        {
            Text = text,
            Foreground = fg ?? new SolidColorBrush(Color.FromRgb(0x5A, 0x7D, 0x95)),
            FontSize = 13, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap,
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
            TxtProxyCount.Text   = $"({_proxies.Count})";
            TxtVisitorCount.Text = $"({_visitors.Count})";
            TxtCounts.Text       = $"{_proxies.Count} 条代理  {_visitors.Count} 条访客";
        }

        protected override void OnClosed(EventArgs e)
        {
            _frpProc?.Kill(true);
            base.OnClosed(e);
        }
    }
}
