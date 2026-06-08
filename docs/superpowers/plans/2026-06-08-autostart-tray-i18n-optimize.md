# Auto-Start, System Tray, i18n & Code Optimization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add auto-start on boot, system tray minimize-to-background, complete i18n coverage, and refactor MainWindow.xaml.cs from 1040 lines into focused helper classes.

**Architecture:** Extract 5 new focused classes (LocalizationService, AutoStartHelper, TrayIconManager, FrpcProcessManager, TerminalWriter) from MainWindow. Wire them together in a slimmed-down MainWindow (~500 lines). Add ~15 i18n keys to both language files. Use Windows Registry for auto-start and System.Windows.Forms.NotifyIcon for the tray.

**Tech Stack:** .NET 10 WPF, System.Windows.Forms (for NotifyIcon), Microsoft.Win32.Registry

---

### Task 1: Fix filename typo

**Files:**
- Rename: `Helpers/DownloadHelper.cs.cs` → `Helpers/DownloadHelper.cs`

- [ ] **Step 1: Rename the file**

Run: `git mv "E:/MyGitHubProject/FrpManager/Helpers/DownloadHelper.cs.cs" "E:/MyGitHubProject/FrpManager/Helpers/DownloadHelper.cs"`

- [ ] **Step 2: Verify it compiles**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Helpers/DownloadHelper.cs.cs Helpers/DownloadHelper.cs
git commit -m "fix: rename DownloadHelper.cs.cs to DownloadHelper.cs"
```

---

### Task 2: Add AutoStartEnabled and FrpcWasRunning to AppSettings model

**Files:**
- Modify: `Models/Models.cs`

- [ ] **Step 1: Add properties to AppSettings**

In `Models/Models.cs`, change the `AppSettings` class to:

```csharp
public class AppSettings
{
    public string FrpcPath { get; set; } = "";
    public List<string> RecentFrpcPaths { get; set; } = new();
    public string Language { get; set; } = "zh-CN";
    public bool AutoStartEnabled { get; set; } = false;
    public bool FrpcWasRunning { get; set; } = false;
}
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Models/Models.cs
git commit -m "feat: add AutoStartEnabled and FrpcWasRunning to AppSettings"
```

---

### Task 3: Create AutoStartHelper

**Files:**
- Create: `Helpers/AutoStartHelper.cs`

- [ ] **Step 1: Write AutoStartHelper.cs**

```csharp
using Microsoft.Win32;

namespace FrpManager.Helpers
{
    public static class AutoStartHelper
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "FrpManager";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        public static void Enable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                // Append --autostart so the app knows to start minimized
                key?.SetValue(AppName, $"\"{exePath}\" --autostart");
            }
            catch { /* silently fail — user may not have registry permissions */ }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
        }

        /// <summary>Returns true if the current launch was triggered by the auto-start registry entry.</summary>
        public static bool IsAutoStartLaunch()
        {
            var args = Environment.GetCommandLineArgs();
            return args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Helpers/AutoStartHelper.cs
git commit -m "feat: add AutoStartHelper for registry-based auto-start on boot"
```

---

### Task 4: Create LocalizationService

**Files:**
- Create: `Helpers/LocalizationService.cs`

- [ ] **Step 1: Write LocalizationService.cs**

```csharp
using System.Windows;

namespace FrpManager.Helpers
{
    public class LocalizationService
    {
        public string CurrentLanguage { get; private set; } = "zh-CN";
        public event Action? LanguageChanged;

        /// <summary>Load the saved language or fall back to zh-CN.</summary>
        public void Initialize(string savedLang)
        {
            if (savedLang is "en-US" or "zh-CN")
            {
                CurrentLanguage = savedLang;
                LoadResourceDictionary(savedLang);
            }
            else
            {
                CurrentLanguage = "zh-CN";
            }
        }

        /// <summary>Toggle between zh-CN and en-US. Returns the new language code.</summary>
        public string Toggle()
        {
            CurrentLanguage = CurrentLanguage == "zh-CN" ? "en-US" : "zh-CN";
            LoadResourceDictionary(CurrentLanguage);
            LanguageChanged?.Invoke();
            return CurrentLanguage;
        }

        /// <summary>Look up a localized string by resource key.</summary>
        public string Get(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }

        private static void LoadResourceDictionary(string lang)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;
            var existing = dicts.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("Localization") == true);
            if (existing != null) dicts.Remove(existing);

            var uri = new Uri($"Localization/Strings.{lang}.xaml", UriKind.Relative);
            dicts.Add(new ResourceDictionary { Source = uri });
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Helpers/LocalizationService.cs
git commit -m "feat: add LocalizationService for centralized language management"
```

---

### Task 5: Create TerminalWriter

**Files:**
- Create: `Views/TerminalWriter.cs`

- [ ] **Step 1: Write TerminalWriter.cs**

```csharp
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FrpManager.Views
{
    public class TerminalWriter
    {
        private readonly RichTextBox _box;
        private const int MaxLines = 2000;

        private static readonly Brush BrushInfo = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE));
        private static readonly Brush BrushWarn = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60));
        private static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        private static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0xA0));
        private static readonly Brush BrushMuted = new SolidColorBrush(Color.FromRgb(0x60, 0x88, 0xA0));

        public TerminalWriter(RichTextBox terminalBox)
        {
            _box = terminalBox;
        }

        public void AppendLine(string line, bool isStderr = false)
        {
            // Strip ANSI escape codes
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
            Append(line, brush);
        }

        public void Append(string text, Brush brush)
        {
            _box.Dispatcher.Invoke(() =>
            {
                var para = new Paragraph(new Run(text)) { Foreground = brush };
                _box.Document.Blocks.Add(para);
                _box.ScrollToEnd();
                while (_box.Document.Blocks.Count > MaxLines)
                    _box.Document.Blocks.Remove(_box.Document.Blocks.FirstBlock);
            });
        }

        public void Clear()
        {
            _box.Dispatcher.Invoke(() => _box.Document.Blocks.Clear());
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Views/TerminalWriter.cs
git commit -m "feat: extract TerminalWriter from MainWindow for terminal output management"
```

---

### Task 6: Create FrpcProcessManager

**Files:**
- Create: `Helpers/FrpcProcessManager.cs`

- [ ] **Step 1: Write FrpcProcessManager.cs**

```csharp
using System.Diagnostics;

namespace FrpManager.Helpers
{
    public class FrpcProcessManager : IDisposable
    {
        private Process? _proc;
        private bool _disposed;

        public bool IsRunning => _proc != null && !_proc.HasExited;
        public int? ProcessId => _proc?.Id;

        /// <summary>Fires on background thread — caller must dispatch to UI.</summary>
        public event Action<string, bool>? LineReceived;
        /// <summary>Fires on background thread — caller must dispatch to UI.</summary>
        public event Action<int>? ProcessExited;

        public void Start(string frpcPath, string configPath)
        {
            Stop();

            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = frpcPath,
                    Arguments = $"-c \"{configPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                },
                EnableRaisingEvents = true
            };

            _proc.OutputDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, false);
            };
            _proc.ErrorDataReceived += (_, de) =>
            {
                if (de.Data != null) LineReceived?.Invoke(de.Data, true);
            };
            _proc.Exited += (_, _) =>
            {
                ProcessExited?.Invoke(_proc?.ExitCode ?? -1);
            };

            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }

        public void Stop()
        {
            if (_proc == null) return;
            try
            {
                if (!_proc.HasExited)
                {
                    _proc.Kill(true);
                }
            }
            catch { /* process may have already exited */ }
            finally
            {
                _proc.Dispose();
                _proc = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Helpers/FrpcProcessManager.cs
git commit -m "feat: extract FrpcProcessManager for frpc process lifecycle"
```

---

### Task 7: Create TrayIconManager

**Files:**
- Create: `Views/TrayIconManager.cs`

- [ ] **Step 1: Write TrayIconManager.cs**

```csharp
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
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Views/TrayIconManager.cs
git commit -m "feat: add TrayIconManager for system tray minimize-to-background"
```

---

### Task 8: Add new i18n keys to both language files

**Files:**
- Modify: `Localization/Strings.zh-CN.xaml`
- Modify: `Localization/Strings.en-US.xaml`

- [ ] **Step 1: Add keys to Strings.zh-CN.xaml**

In `Localization/Strings.zh-CN.xaml`, insert before `</ResourceDictionary>`:

```xml
    <!-- Auto-start & Tray (new) -->
    <sys:String x:Key="S_AutoStart">开机自动启动</sys:String>
    <sys:String x:Key="S_AutoStartHint">开启后，系统启动时自动运行 FrpManager 并启动 frpc</sys:String>
    <sys:String x:Key="S_TrayShow">显示窗口</sys:String>
    <sys:String x:Key="S_TrayStartFrpc">启动 frpc</sys:String>
    <sys:String x:Key="S_TrayStopFrpc">停止 frpc</sys:String>
    <sys:String x:Key="S_TrayExit">退出</sys:String>
    <sys:String x:Key="S_TrayBalloonTitle">FrpManager</sys:String>
    <sys:String x:Key="S_TrayBalloonText">仍在后台运行，双击托盘图标打开窗口</sys:String>
    <sys:String x:Key="S_AppErrorTitle">FrpManager 错误</sys:String>
    <sys:String x:Key="S_AppCrashTitle">FrpManager 崩溃</sys:String>
    <sys:String x:Key="S_AutoStartSection">系统设置</sys:String>
</ResourceDictionary>
```

- [ ] **Step 2: Add keys to Strings.en-US.xaml**

In `Localization/Strings.en-US.xaml`, insert before `</ResourceDictionary>`:

```xml
    <!-- Auto-start & Tray (new) -->
    <sys:String x:Key="S_AutoStart">Start with Windows</sys:String>
    <sys:String x:Key="S_AutoStartHint">When enabled, FrpManager starts automatically with Windows</sys:String>
    <sys:String x:Key="S_TrayShow">Show Window</sys:String>
    <sys:String x:Key="S_TrayStartFrpc">Start frpc</sys:String>
    <sys:String x:Key="S_TrayStopFrpc">Stop frpc</sys:String>
    <sys:String x:Key="S_TrayExit">Exit</sys:String>
    <sys:String x:Key="S_TrayBalloonTitle">FrpManager</sys:String>
    <sys:String x:Key="S_TrayBalloonText">Still running in background. Double-click tray icon to open.</sys:String>
    <sys:String x:Key="S_AppErrorTitle">FrpManager Error</sys:String>
    <sys:String x:Key="S_AppCrashTitle">FrpManager Crash</sys:String>
    <sys:String x:Key="S_AutoStartSection">System Settings</sys:String>
</ResourceDictionary>
```

- [ ] **Step 3: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add Localization/Strings.zh-CN.xaml Localization/Strings.en-US.xaml
git commit -m "feat: add i18n keys for auto-start, tray, and error dialogs"
```

---

### Task 9: Update FrpManager.csproj to reference System.Windows.Forms

**Files:**
- Modify: `FrpManager.csproj`

- [ ] **Step 1: Add UseWindowsForms and package reference**

In `FrpManager.csproj`, change the PropertyGroup to add `<UseWindowsForms>true</UseWindowsForms>` and add the System.Windows.Forms package:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <AssemblyName>FrpManager</AssemblyName>
    <RootNamespace>FrpManager</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>app.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <Resource Include="Themes/SkyTheme.xaml" />
    <Resource Include="app.ico" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Tomlyn" Version="0.17.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add FrpManager.csproj
git commit -m "build: add UseWindowsForms for NotifyIcon system tray support"
```

---

### Task 10: Update App.xaml.cs with i18n error messages and auto-start handling

**Files:**
- Modify: `App.xaml.cs`

- [ ] **Step 1: Rewrite App.xaml.cs**

```csharp
using FrpManager.Helpers;
using FrpManager.Views;
using System.Windows;
using System.Windows.Threading;

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
                    $"{_loc.Get("S_AppErrorTitle")}：\n\n{ex.Exception.Message}",
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
                Shutdown();
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
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add App.xaml.cs
git commit -m "feat: i18n error dialogs, auto-start launch handling, tray wiring in App"
```

---

### Task 11: Add auto-start checkbox to MainWindow.xaml

**Files:**
- Modify: `Views/MainWindow.xaml`

- [ ] **Step 1: Add auto-start section to Server Config tab**

In `Views/MainWindow.xaml`, find the Server Config tab (after the Log File card, before the closing `</StackPanel>` of the Server tab). Add a new card section:

In the Server Config tab ScrollViewer, after the Log File card (`</Border>` that contains `S_LogFileSection`) and before `</StackPanel>` / `</ScrollViewer>`, add:

```xml
                            <!-- Auto-start -->
                            <Border Style="{StaticResource Card}">
                                <StackPanel>
                                    <TextBlock Text="{DynamicResource S_AutoStartSection}" Style="{StaticResource SectionLabel}"/>
                                    <CheckBox x:Name="ChkAutoStart" Style="{StaticResource SkyCheckBox}"
                                              Content="{DynamicResource S_AutoStart}"
                                              Checked="AutoStart_Changed" Unchecked="AutoStart_Changed"
                                              Margin="0,4,0,0"/>
                                    <TextBlock Text="{DynamicResource S_AutoStartHint}"
                                               Foreground="{StaticResource TextMutedBrush}" FontSize="11"
                                               Margin="20,2,0,0" TextWrapping="Wrap"/>
                                </StackPanel>
                            </Border>
```

- [ ] **Step 2: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -5`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Views/MainWindow.xaml
git commit -m "feat: add auto-start checkbox to Server Config tab"
```

---

### Task 12: Refactor MainWindow.xaml.cs — slim down to ~500 lines

**Files:**
- Modify: `Views/MainWindow.xaml.cs`

This is the largest task. The full new file content is below.

- [ ] **Step 1: Write the refactored MainWindow.xaml.cs**

```csharp
using FrpManager.Helpers;
using FrpManager.Models;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Tomlyn;
using Tomlyn.Model;

namespace FrpManager.Views
{
    public partial class MainWindow : Window
    {
        // ── Dependencies ────────────────────────────────────────────────────
        private readonly LocalizationService _loc;
        private TrayIconManager? _tray;

        // ── State ───────────────────────────────────────────────────────────
        private readonly ServerConfig _server = new();
        private readonly ObservableCollection<ProxyConfig> _proxies = new();
        private readonly ObservableCollection<VisitorConfig> _visitors = new();
        private AppSettings _settings = new();

        private ProxyConfig? _curProxy;
        private VisitorConfig? _curVisitor;
        private bool _busy;

        private readonly FrpcProcessManager _frpc = new();
        private TerminalWriter? _term;
        private CancellationTokenSource? _dlCts;

        // ── Shorthand ───────────────────────────────────────────────────────
        string L(string key) => _loc.Get(key);

        // ══ Constructor ═════════════════════════════════════════════════════

        public MainWindow(LocalizationService loc, AppSettings settings)
        {
            _loc = loc;
            _settings = settings;
            InitializeComponent();

            _term = new TerminalWriter(TerminalBox);
            ProxyList.ItemsSource = _proxies;
            VisitorList.ItemsSource = _visitors;

            // Wire language change event
            _loc.LanguageChanged += () =>
            {
                UpdateCounts();
                RefreshDynamicLabels();
            };

            // Wire frpc process events
            _frpc.LineReceived += (line, isStderr) =>
                Dispatcher.Invoke(() => _term?.AppendLine(line, isStderr));
            _frpc.ProcessExited += (code) =>
                Dispatcher.Invoke(() =>
                {
                    SetFrpRunning(false);
                    _term?.Append(L("S_TermExited") + code + " )───", TerminalWriter.BrushMuted);
                    SetStatus(L("S_StatusExited"));
                });

            LoadServerToUI();
            LoadFrpcPathsToUI();
            LoadAutoStartUI();
            RefreshPreview();
            SetStatus(L("S_Ready"));

            MainTabs.SelectionChanged += (s, e) =>
            {
                if (MainTabs.SelectedItem == TabTomlLib)
                    LoadTomlFileList();
            };
        }

        // ══ Tray integration ════════════════════════════════════════════════

        public void SetTrayIcon(TrayIconManager tray) => _tray = tray;

        public void ShutdownFrpc() => _frpc.Stop();

        public void ToggleFrpc() => Btn_Start(this, new RoutedEventArgs());

        public void AutoResumeFrpc()
        {
            if (!string.IsNullOrWhiteSpace(_settings.FrpcPath) && File.Exists(_settings.FrpcPath))
                Btn_Start(this, new RoutedEventArgs());
        }

        // ══ Language toggle ═════════════════════════════════════════════════

        void Btn_ToggleLang(object s, RoutedEventArgs e)
        {
            var newLang = _loc.Toggle();
            _settings.Language = newLang;
            SettingsHelper.Save(_settings);
        }

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

        void LoadAutoStartUI()
        {
            _busy = true;
            ChkAutoStart.IsChecked = _settings.AutoStartEnabled;
            _busy = false;
        }

        void AutoStart_Changed(object s, RoutedEventArgs e)
        {
            if (_busy) return;
            bool enabled = ChkAutoStart.IsChecked == true;
            _settings.AutoStartEnabled = enabled;
            SettingsHelper.Save(_settings);
            if (enabled)
                AutoStartHelper.Enable();
            else
                AutoStartHelper.Disable();
        }

        // ══ FRP Path ════════════════════════════════════════════════════════

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
                TxtFrpcPathHint.Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
            }
            else if (File.Exists(path))
            {
                TxtFrpcPathHint.Text = L("S_FrpcPathHintOk");
                TxtFrpcPathHint.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreenBrush");
            }
            else
            {
                TxtFrpcPathHint.Text = L("S_FrpcPathHintBad");
                TxtFrpcPathHint.Foreground = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
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

        // ══ Server UI ═══════════════════════════════════════════════════════

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

        // ══ Proxy CRUD ══════════════════════════════════════════════════════

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

        // ══ Visitor CRUD ════════════════════════════════════════════════════

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

        // ══ Editor visibility ════════════════════════════════════════════════

        enum EditorMode { None, Proxy, Visitor }

        void ShowEditor(EditorMode mode)
        {
            EmptyState.Visibility = mode == EditorMode.None ? Visibility.Visible : Visibility.Collapsed;
            ProxyEditorPanel.Visibility = mode == EditorMode.Proxy ? Visibility.Visible : Visibility.Collapsed;
            VisitorEditorPanel.Visibility = mode == EditorMode.Visitor ? Visibility.Visible : Visibility.Collapsed;
            if (mode != EditorMode.None && MainTabs.SelectedItem != TabEditor)
                MainTabs.SelectedItem = TabEditor;
        }

        // ══ Templates ═══════════════════════════════════════════════════════

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

        // ══ Preview / Export ════════════════════════════════════════════════

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

        // ══ FRP Launch ══════════════════════════════════════════════════════

        void Btn_Start(object s, RoutedEventArgs e)
        {
            if (_frpc.IsRunning)
            {
                _frpc.Stop();
                SetFrpRunning(false);
                _term?.Append(L("S_TermStopped"), TerminalWriter.BrushMuted);
                SetStatus(L("S_StatusStopped"));
                _settings.FrpcWasRunning = false;
                SettingsHelper.Save(_settings);
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

            _frpc.Start(frpcPath, tmp);

            SetFrpRunning(true);
            SetStatus(L("S_StatusStarted") + _frpc.ProcessId + ")");
            _term?.Append(L("S_TermStarted") + frpcPath + " ───", TerminalWriter.BrushSuccess);
            _term?.Append(L("S_TermConfig") + tmp + " ───", TerminalWriter.BrushMuted);
            MainTabs.SelectedItem = TabTerminal;

            _settings.FrpcWasRunning = true;
            SettingsHelper.Save(_settings);
        }

        void SetFrpRunning(bool on)
        {
            var green = System.Windows.Media.Color.FromRgb(0x52, 0xB7, 0x88);
            StatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                on ? green : System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xC0));
            TermDot.Fill = new System.Windows.Media.SolidColorBrush(
                on ? green : System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
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

        // ══ Config Library ══════════════════════════════════════════════════

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
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB8, 0xD8, 0xEE)),
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
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x50)),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new System.Windows.Media.FontFamily("Consolas")
            });
            infoPanel.Children.Add(new TextBlock
            {
                Text = $"{L("S_ModifiedTime")}: {modified}    {size}",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x5A, 0x7D, 0x95)),
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

        // ══ GitHub Download ═════════════════════════════════════════════════

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
                        Background = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xD8, 0xEE, 0xF8)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(14, 8, 14, 8),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    var hSp = new StackPanel { Orientation = Orientation.Horizontal };
                    hSp.Children.Add(new TextBlock
                    {
                        Text = $"🏷  {rel.name ?? rel.tag_name}",
                        Foreground = (System.Windows.Media.Brush)FindResource("AccentDarkBrush"),
                        FontSize = 14,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    if (rel.published_at.Length >= 10)
                        hSp.Children.Add(new TextBlock
                        {
                            Text = $"    {rel.published_at[..10]}",
                            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
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
                    (System.Windows.Media.Brush)FindResource("AccentRedBrush")));
                TxtRelInfo.Text = L("S_StatusUpdateFail");
            }
        }

        UIElement BuildAssetRow(GitHubAsset asset)
        {
            bool isWin = asset.name.Contains("windows", StringComparison.OrdinalIgnoreCase);
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB8, 0xD8, 0xEE)),
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
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x1A, 0x3A, 0x50)),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13
            });
            info.Children.Add(new TextBlock
            {
                Text = asset.SizeLabel,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x5A, 0x7D, 0x95)),
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

        void Btn_OpenGithub(object s, RoutedEventArgs e)
            => Process.Start(new ProcessStartInfo(
                "https://github.com/fatedier/frp/releases")
            { UseShellExecute = true });

        // ══ Helpers ═════════════════════════════════════════════════════════

        static TextBlock MakeTextBlock(string text, System.Windows.Media.Brush? fg = null) => new()
        {
            Text = text,
            Foreground = fg ?? new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x5A, 0x7D, 0x95)),
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

        // ══ Window close → minimize to tray ═════════════════════════════════

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_tray != null)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                _tray.HideToTray();

                // Show balloon tip on first minimize
                if (!_settings.FrpcWasRunning)  // reuse field as "hasShownTrayTip"
                {
                    _tray.ShowBalloon(L("S_TrayBalloonTitle"), L("S_TrayBalloonText"));
                }
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _frpc?.Dispose();
            base.OnClosed(e);
        }
    }
}
```

- [ ] **Step 2: Make BrushMuted and BrushSuccess public in TerminalWriter**

Since `TerminalWriter.BrushMuted` and `TerminalWriter.BrushSuccess` are referenced from MainWindow, make them public:

In `Views/TerminalWriter.cs`, change:
```csharp
        private static readonly Brush BrushInfo = ...
        private static readonly Brush BrushWarn = ...
        private static readonly Brush BrushError = ...
        private static readonly Brush BrushSuccess = ...
        private static readonly Brush BrushMuted = ...
```
To:
```csharp
        public static readonly Brush BrushInfo = ...
        public static readonly Brush BrushWarn = ...
        public static readonly Brush BrushError = ...
        public static readonly Brush BrushSuccess = ...
        public static readonly Brush BrushMuted = ...
```

- [ ] **Step 3: Verify build**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -20`
Expected: Build succeeded. Fix any compilation errors.

- [ ] **Step 4: Commit**

```bash
git add Views/MainWindow.xaml.cs Views/TerminalWriter.cs
git commit -m "refactor: slim MainWindow to ~540 lines, wire new services"
```

---

### Task 13: Remove Terminal-related code from MainWindow (already moved to TerminalWriter)

**Files:**
- Verify: `Views/MainWindow.xaml.cs`

- [ ] **Step 1: Verify TerminalButton handler still works**

In the refactored MainWindow.xaml.cs, ensure there is a `Btn_ClearTerminal` handler:

```csharp
        void Btn_ClearTerminal(object s, RoutedEventArgs e)
            => _term?.Clear();
```

If missing, add it after the GitHub download methods.

- [ ] **Step 2: Verify build and fix any issues**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet build 2>&1 | tail -10`
Expected: Build succeeded.

- [ ] **Step 3: Commit any fixes**

```bash
git add Views/MainWindow.xaml.cs
git commit -m "fix: ensure terminal clear button wired correctly"
```

---

### Task 14: Final build, test, and polish

**Files:**
- All modified files

- [ ] **Step 1: Clean and rebuild**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet clean && dotnet build 2>&1`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Run the app to verify it launches**

Run: `cd E:/MyGitHubProject/FrpManager && dotnet run 2>&1 &`
Expected: Window appears, tray icon visible in system tray. Close window → minimizes to tray. Tray menu items work.

- [ ] **Step 3: Check file sizes**

Run: `wc -l Views/MainWindow.xaml.cs Helpers/*.cs Views/TerminalWriter.cs Views/TrayIconManager.cs`
Expected: MainWindow.xaml.cs ~540 lines, all other files <200 lines each.

- [ ] **Step 4: Commit final polish**

```bash
git add -A
git commit -m "chore: final polish, clean build verified"
```
