# FrpManager — Auto-Start, System Tray, i18n & Code Optimization

**Date:** 2026-06-08
**Status:** Design approved

---

## 1. Features

### 1.1 Auto-Start on Boot

- **Registry-based**: Write/remove `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FrpManager`
- **UI Toggle**: Checkbox "开机自动启动 / Start with Windows" in Server Config tab
- **Settings**: `AutoStartEnabled` bool persisted in `AppSettings` → `settings.json`
- **Smart resume**: If auto-start launches the app AND frpc was running last session, auto-launch frpc
- **Startup behavior**: When launched via auto-start, the app starts minimized to tray (no window flash)

Implementation: new `Helpers/AutoStartHelper.cs` static class.

### 1.2 System Tray & Silent Background

- **Tray icon**: Uses `System.Windows.Forms.NotifyIcon` with the app icon
- **Close behavior**: Window X button → hides to tray (does NOT exit)
- **Tray context menu**:
  - "显示窗口 / Show Window" — restores the main window
  - "启动 frpc / Start frpc" — starts frpc if not running (dynamic label)
  - "停止 frpc / Stop frpc" — stops frpc if running (dynamic label)
  - "退出 / Exit" — kills frpc then fully exits application
- **Double-click tray icon** → restore window
- **Balloon tip**: On first minimize, show "FrpManager 仍在后台运行 / FrpManager is still running in the background"
- **Window state**: `WindowState.Minimized` + `ShowInTaskbar = false` when hidden; restored on show

Implementation: new `Views/TrayIconManager.cs` class (owns NotifyIcon lifecycle).

### 1.3 i18n Completion

- Add ~15 new resource keys for tray menu items, auto-start label, balloon tip, first-run notice
- Fix hardcoded Chinese strings in `App.xaml.cs` (crash/error dialogs) — use resource lookup
- New strings in both `Strings.zh-CN.xaml` and `Strings.en-US.xaml`

---

## 2. Code Optimization

### 2.1 File Extraction Plan

| Current | Extracted To | Lines | Responsibility |
|---------|-------------|-------|----------------|
| `MainWindow.xaml.cs` (~1041) | After extraction (~500) | ~500 | UI wiring, CRUD, templates |
| (inline in MainWindow) | `Helpers/LocalizationService.cs` | ~80 | Language load/toggle/lookup, event |
| (inline in MainWindow) | `Helpers/FrpcProcessManager.cs` | ~180 | Process lifecycle, stdout/stderr, events |
| (inline in MainWindow) | `Views/TerminalWriter.cs` | ~60 | Terminal output, color classification |
| (new) | `Helpers/AutoStartHelper.cs` | ~45 | Registry read/write/delete |
| (new) | `Views/TrayIconManager.cs` | ~120 | NotifyIcon, context menu, minimize/restore |

### 2.2 Other Fixes

- Rename `Helpers/DownloadHelper.cs.cs` → `Helpers/DownloadHelper.cs` (typo in filename)
- `App.xaml.cs` error dialogs: replace hardcoded Chinese with localization keys
- Extract `EditorMode` enum and editor visibility logic into a small helper or region
- Use `nameof()` in `INotifyPropertyChanged` calls where string literals exist

---

## 3. New Files Detail

### `Helpers/AutoStartHelper.cs`
```csharp
public static class AutoStartHelper
{
    static readonly string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    static readonly string AppName = "FrpManager";

    public static bool IsEnabled();
    public static void Enable();   // writes registry key pointing to exe
    public static void Disable();  // removes registry key
}
```

### `Helpers/LocalizationService.cs`
```csharp
public class LocalizationService
{
    public string CurrentLanguage { get; private set; } // "zh-CN" or "en-US"
    public event Action? LanguageChanged;

    public void Initialize(string savedLang);  // load saved or default
    public void Toggle();                       // switch zh-CN <-> en-US
    public string Get(string key);              // TryFindResource wrapper
}
```

### `Helpers/FrpcProcessManager.cs`
```csharp
public class FrpcProcessManager : IDisposable
{
    public bool IsRunning { get; }
    public event Action<string>? OutputReceived;   // already classified
    public event Action? ProcessExited;

    public void Start(string frpcPath, string configPath);
    public void Stop();
    public void Dispose();
}
```

### `Views/TrayIconManager.cs`
```csharp
public class TrayIconManager : IDisposable
{
    public event Action? ShowWindowRequested;
    public event Action? ExitRequested;
    public event Action? ToggleFrpcRequested;

    public TrayIconManager(Window owner, LocalizationService loc);
    public void ShowBalloon(string title, string text);
    public void UpdateFrpcState(bool isRunning);  // updates Start/Stop menu text
    public void Dispose();
}
```

### `Views/TerminalWriter.cs`
```csharp
public class TerminalWriter
{
    public TerminalWriter(RichTextBox terminalBox);

    public void AppendLine(string line, bool isStderr = false);
    public void Append(string text, Brush brush);
    public void Clear();
}
```

---

## 4. Modified Files

| File | Changes |
|------|---------|
| `Models/Models.cs` | Add `AutoStartEnabled` to `AppSettings`, add `FrpcWasRunning` bool |
| `Helpers/SettingsHelper.cs` | No structural changes needed |
| `App.xaml.cs` | i18n error messages, wire LocalizationService init, check auto-start launch arg |
| `App.xaml` | No changes needed |
| `Views/MainWindow.xaml` | Add auto-start checkbox in Server Config tab, tray-related UI bindings |
| `Views/MainWindow.xaml.cs` | Slim down to ~500 lines, delegate to new classes |
| `Localization/Strings.zh-CN.xaml` | Add ~15 new keys |
| `Localization/Strings.en-US.xaml` | Add ~15 new keys |
| `FrpManager.csproj` | Add `System.Windows.Forms` reference for NotifyIcon, add `Microsoft.Extensions.DependencyInjection` or manual DI |

---

## 5. Startup Flow

```
App.OnStartup()
├── LocalizationService.Initialize(settings.Language)
├── Check if launched with --autostart argument (registry run key can pass this)
│   └── If yes: mainWindow.HideToTray() immediately
├── Show MainWindow (or hidden if auto-start)
├── If settings.FrpcWasRunning && settings.AutoStartEnabled:
│   └── FrpcProcessManager.Start(frpcPath, configPath)
└── Wire TrayIconManager events

On Window Close (X button):
├── e.Cancel = true
├── HideToTray()
└── Show balloon (first time only)

On Tray Exit:
├── FrpcProcessManager.Stop()
├── settings.FrpcWasRunning = false
├── SettingsHelper.Save()
├── TrayIconManager.Dispose()
└── Application.Current.Shutdown()
```

---

## 6. New i18n Keys

| Key | zh-CN | en-US |
|-----|-------|-------|
| `S_AutoStart` | 开机自动启动 | Start with Windows |
| `S_TrayShow` | 显示窗口 | Show Window |
| `S_TrayStartFrpc` | 启动 frpc | Start frpc |
| `S_TrayStopFrpc` | 停止 frpc | Stop frpc |
| `S_TrayExit` | 退出 | Exit |
| `S_TrayBalloonTitle` | FrpManager | FrpManager |
| `S_TrayBalloonText` | 仍在后台运行，双击图标打开窗口 | Still running in background. Double-click to open. |
| `S_AppError` | 运行时错误 | Runtime Error |
| `S_AppCrash` | 崩溃 | Crash |
| `S_AutoStartHint` | 开启后，系统启动时自动运行 FrpManager | When enabled, FrpManager starts automatically with Windows |
