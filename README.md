# FrpManager v3 — 可视化 FRP 配置工具

柔和天蓝风格，基于 .NET 10 WPF 开发。

---

## 功能一览

| 功能 | 说明 |
|---|---|
| 🎛 代理编辑器 | 支持 TCP / UDP / HTTP / HTTPS / STCP / XTCP 全类型 |
| 🖥 服务器配置 | 地址、端口、Token、TLS、日志级别一站配置 |
| ⚡ 快速模板 | SSH / RDP / HTTP / HTTPS / UDP / Minecraft / DNS |
| 📄 配置预览 | 实时生成 frpc.toml，一键复制 / 导出 |
| ⬇ GitHub 下载 | 直接拉取最新 Release，带进度条 |
| ▶ 启动 / 停止 frpc | 直接在应用内管理进程 |

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

## 打包发布（修复无法打开问题）

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
