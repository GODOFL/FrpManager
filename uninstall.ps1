# FrpManager 一键卸载脚本
# 用法：右键 uninstall.bat → 以管理员身份运行（非必须，普通权限即可）

$ErrorActionPreference = "SilentlyContinue"
$Host.UI.RawUI.WindowTitle = "FrpManager 卸载"

$AppName = "FrpManager"
$RegPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$RegName = $AppName
$DataDir = "$env:APPDATA\$AppName"

# ── 1. 终止运行中的进程 ────────────────────────────────────────────────
$procs = Get-Process -Name $AppName -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "终止 $AppName 进程..." -ForegroundColor Yellow
    $procs | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Write-Host "  已终止。" -ForegroundColor Green
} else {
    Write-Host "$AppName 未在运行。" -ForegroundColor Gray
}

# ── 2. 收集待清理项 ────────────────────────────────────────────────────
$items = @()

# 注册表自启动
if (Test-Path "$RegPath\$RegName") {
    $items += "注册表自启动项 (HKCU\...\Run\$RegName)"
}

# 配置目录
if (Test-Path $DataDir) {
    $items += "配置目录 ($DataDir)"
}

# 尝试推断安装目录
$installDir = $null
# 方法1：从注册表自启动项读取路径
$regValue = Get-ItemProperty -Path $RegPath -Name $RegName -ErrorAction SilentlyContinue
if ($regValue -and $regValue.$RegName) {
    $exePath = $regValue.$RegName -replace '"', ''
    $dir = Split-Path $exePath -Parent
    if (Test-Path $dir) {
        $installDir = $dir
    }
}
# 方法2：从运行中进程获取
if (-not $installDir) {
    try {
        $procPath = (Get-CimInstance Win32_Process -Filter "name='$AppName.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1).ExecutablePath
        if ($procPath) {
            $installDir = Split-Path $procPath -Parent
        }
    } catch { }
}
# 方法3：常见目录
if (-not $installDir) {
    $commonDirs = @(
        "$env:LOCALAPPDATA\Programs\$AppName",
        "$env:ProgramFiles\$AppName",
        "${env:ProgramFiles(x86)}\$AppName",
        "$env:USERPROFILE\Desktop\$AppName"
    )
    foreach ($d in $commonDirs) {
        if (Test-Path $d) {
            $installDir = $d
            break
        }
    }
}

if ($installDir -and (Test-Path $installDir)) {
    $items += "安装目录 ($installDir)"
}

# ── 3. 若无可清理项则直接退出 ──────────────────────────────────────────
if ($items.Count -eq 0) {
    Write-Host "`n未发现任何 FrpManager 相关项，无需清理。" -ForegroundColor Green
    Write-Host "按任意键退出..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 0
}

# ── 4. 确认 ────────────────────────────────────────────────────────────
Write-Host "`n========== 将清理以下内容 ==========" -ForegroundColor Cyan
foreach ($item in $items) {
    Write-Host "  • $item" -ForegroundColor White
}
Write-Host "====================================" -ForegroundColor Cyan

$shell = New-Object -ComObject WScript.Shell
$result = $shell.Popup(
    "确定要卸载 FrpManager 吗？`n`n将删除以上列出的所有内容。",
    0,
    "FrpManager 卸载确认",
    1 + 48  # OK+Cancel + Warning icon
)

if ($result -ne 1) {
    Write-Host "`n已取消。" -ForegroundColor Gray
    exit 0
}

# ── 5. 执行清理 ────────────────────────────────────────────────────────
$removed = @()
$failed = @()

Write-Host "`n正在清理..." -ForegroundColor Yellow

# 5a. 删除注册表自启动
if (Test-Path "$RegPath\$RegName") {
    try {
        Remove-ItemProperty -Path $RegPath -Name $RegName -Force -ErrorAction Stop
        $removed += "注册表自启动项"
        Write-Host "  ✓ 已删除注册表自启动项" -ForegroundColor Green
    } catch {
        $failed += "注册表自启动项 ($($_.Exception.Message))"
        Write-Host "  ✗ 删除注册表失败: $_" -ForegroundColor Red
    }
}

# 5b. 删除配置目录
if (Test-Path $DataDir) {
    try {
        Remove-Item -Path $DataDir -Recurse -Force -ErrorAction Stop
        $removed += "配置目录"
        Write-Host "  ✓ 已删除配置目录" -ForegroundColor Green
    } catch {
        $failed += "配置目录 ($($_.Exception.Message))"
        Write-Host "  ✗ 删除配置目录失败: $_" -ForegroundColor Red
    }
}

# 5c. 删除安装目录
if ($installDir -and (Test-Path $installDir)) {
    $shell = New-Object -ComObject WScript.Shell
    $result = $shell.Popup(
        "是否同时删除安装目录？`n`n$installDir`n`n注意：如果该目录中包含其他文件，它们也将被删除。",
        0,
        "删除安装目录？",
        1 + 48
    )
    if ($result -eq 1) {
        try {
            Remove-Item -Path $installDir -Recurse -Force -ErrorAction Stop
            $removed += "安装目录"
            Write-Host "  ✓ 已删除安装目录" -ForegroundColor Green
        } catch {
            $failed += "安装目录 ($($_.Exception.Message))"
            Write-Host "  ✗ 删除安装目录失败: $_" -ForegroundColor Red
        }
    } else {
        Write-Host "  - 已跳过安装目录" -ForegroundColor Gray
    }
}

# ── 6. 结果 ────────────────────────────────────────────────────────────
Write-Host "`n========== 卸载完成 ==========" -ForegroundColor Cyan
if ($removed.Count -gt 0) {
    Write-Host "  已清理：" -ForegroundColor Green
    foreach ($r in $removed) {
        Write-Host "    ✓ $r" -ForegroundColor Green
    }
}
if ($failed.Count -gt 0) {
    Write-Host "  失败：" -ForegroundColor Red
    foreach ($f in $failed) {
        Write-Host "    ✗ $f" -ForegroundColor Red
    }
}
Write-Host "==============================" -ForegroundColor Cyan

Write-Host "`n按任意键退出..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
