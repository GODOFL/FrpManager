# FrpManager — 可视化 FRP 配置管理工具

FrpManager 是一个面向 Windows 的 FRP 图形化管理工具，基于 .NET 10 WPF 开发。
它可以编辑 `frpc.toml` / `frps.toml`、管理 tomlset 配置库、启动或停止 frpc，并支持开机自启动、托盘后台运行和本地一键打包。

---

## 功能一览

| 功能 | 说明 |
|---|---|
| 🎛 代理 / 访客编辑器 | 支持 TCP / UDP / HTTP / HTTPS / STCP / XTCP / SUDP 配置 |
| 🖥 服务器配置 | 地址、端口、认证、Token、TLS、日志级别一站式配置 |
| 📁 tomlset 配置库 | 管理多个 TOML 配置文件，支持自定义顺序；开机自启动默认加载排序第一项 |
| ⚡ 快速模板 | 内置 SSH、RDP、Web、HTTPS、UDP、Minecraft、STCP、XTCP、SUDP 等模板 |
| 📄 配置预览 | 实时生成 `frpc.toml`，支持复制、打开、保存和导出 |
| 🖥 终端输出 | 在应用内查看 frpc stdout / stderr 输出，自动换行和高亮错误日志 |
| ⬇ FRP 下载 | 直接拉取 FRP Release，下载并自动识别 frpc.exe |
| ▶ 进程管理 | 一键启动 / 停止 frpc，后台托盘运行，托盘菜单可显示、启停和退出 |
| 🚀 系统集成 | 支持开机自启动、单实例运行保护、发布包图标和卸载脚本 |

---

## 环境要求

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

---

## 开发运行

```bash
cd FrpManager
dotnet restore
dotnet run
```

---

### 方式一：框架依赖（体积小，目标机需安装 .NET 10）
```bash
dotnet publish -c Release -r win-x64
```
生成在 `bin\Release\net10.0-windows\win-x64\publish\`

### 方式二：自包含单文件（推荐，无需安装运行时）
```bash
dotnet publish -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

> ⚠️ 注意：**不要**使用 `dotnet publish` 默认输出的 dll 直接双击运行。
> 请始终使用上面的命令打包，或直接运行 `dotnet run`。

---

## 项目结构

```
FrpManager/
├── App.xaml / App.xaml.cs        ← 入口 + 全局异常捕获
├── Themes/SkyTheme.xaml          ← 天蓝柔和主题
├── Models/Models.cs              ← 数据模型
├── Helpers/
│   ├── ConfigHelper.cs           ← TOML 生成 + 模板
│   └── GithubHelper.cs           ← GitHub API + 下载
└── Views/
    ├── MainWindow.xaml           ← UI 布局
    └── MainWindow.xaml.cs        ← 业务逻辑
```
