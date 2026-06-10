# FrpManager local uninstaller.
# Run from the published application folder. The script removes the current
# user's auto-start entry, stops running FrpManager processes, and optionally
# removes app files and user data after confirmation.

$ErrorActionPreference = "Stop"
$AppName = "FrpManager"
$RunKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$ScriptDir = if ($PSScriptRoot) {
    [System.IO.Path]::GetFullPath($PSScriptRoot)
} else {
    [System.IO.Path]::GetFullPath((Get-Location).Path)
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Confirm-Action {
    param(
        [string]$Question,
        [bool]$DefaultYes = $false
    )

    $suffix = if ($DefaultYes) { "[Y/n]" } else { "[y/N]" }
    while ($true) {
        $answer = Read-Host "$Question $suffix"
        if ([string]::IsNullOrWhiteSpace($answer)) {
            return $DefaultYes
        }

        switch ($answer.Trim().ToLowerInvariant()) {
            "y" { return $true }
            "yes" { return $true }
            "n" { return $false }
            "no" { return $false }
            default { Write-Host "Please answer y or n." -ForegroundColor Yellow }
        }
    }
}

function Test-SafeInstallDirectory {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root = [System.IO.Path]::GetPathRoot($full).TrimEnd('\')
    if ($full -eq $root) {
        return $false
    }

    $exe = Join-Path $full "$AppName.exe"
    $script = Join-Path $full "uninstall.ps1"
    return (Test-Path -LiteralPath $exe -PathType Leaf) -and
           (Test-Path -LiteralPath $script -PathType Leaf)
}

function Remove-PathIfExists {
    param(
        [string]$Path,
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Host "Skip ${Label}: not found" -ForegroundColor DarkGray
        return
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Host "Removed ${Label}: $Path" -ForegroundColor Green
    } catch {
        Write-Host "Failed to remove ${Label}: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Start-DelayedInstallDirRemoval {
    param([string]$InstallDir)

    $tempCmd = Join-Path $env:TEMP ("FrpManager-uninstall-{0}.cmd" -f ([guid]::NewGuid().ToString("N")))
    $escapedInstallDir = $InstallDir.Replace('"', '""')
    $cmd = @"
@echo off
ping 127.0.0.1 -n 3 >nul
rmdir /s /q "$escapedInstallDir"
del "%~f0" >nul 2>nul
"@

    Set-Content -LiteralPath $tempCmd -Value $cmd -Encoding ASCII
    Start-Process -FilePath "cmd.exe" -ArgumentList "/c `"$tempCmd`"" -WindowStyle Hidden
    Write-Host "Scheduled install directory removal: $InstallDir" -ForegroundColor Green
}

Write-Host "FrpManager Uninstaller" -ForegroundColor Cyan
Write-Host "Script directory: $ScriptDir"

Write-Step "Stopping running processes"
$processes = Get-Process -Name $AppName -ErrorAction SilentlyContinue
if ($processes) {
    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.Id -Force
            Write-Host "Stopped $AppName process: PID $($process.Id)" -ForegroundColor Green
        } catch {
            Write-Host "Failed to stop PID $($process.Id): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    Start-Sleep -Milliseconds 500
} else {
    Write-Host "No running $AppName process found." -ForegroundColor DarkGray
}

Write-Step "Removing auto-start entry"
try {
    $runValue = Get-ItemProperty -Path $RunKeyPath -Name $AppName -ErrorAction SilentlyContinue
    if ($runValue) {
        Remove-ItemProperty -Path $RunKeyPath -Name $AppName -Force
        Write-Host "Removed HKCU Run entry: $AppName" -ForegroundColor Green
    } else {
        Write-Host "No HKCU Run entry found." -ForegroundColor DarkGray
    }
} catch {
    Write-Host "Failed to remove HKCU Run entry: $($_.Exception.Message)" -ForegroundColor Red
}

$userDataDirs = @(
    (Join-Path $env:APPDATA $AppName),
    (Join-Path $env:LOCALAPPDATA $AppName)
) | Select-Object -Unique

Write-Step "User data"
$existingDataDirs = $userDataDirs | Where-Object { Test-Path -LiteralPath $_ }
if ($existingDataDirs.Count -gt 0) {
    Write-Host "Found user data directories:"
    foreach ($dir in $existingDataDirs) {
        Write-Host "  $dir"
    }

    if (Confirm-Action "Remove user data and saved settings?" $false) {
        foreach ($dir in $existingDataDirs) {
            Remove-PathIfExists -Path $dir -Label "user data"
        }
    } else {
        Write-Host "User data kept." -ForegroundColor Yellow
    }
} else {
    Write-Host "No user data directory found." -ForegroundColor DarkGray
}

Write-Step "Application files"
if (Test-SafeInstallDirectory -Path $ScriptDir) {
    Write-Host "Install directory:"
    Write-Host "  $ScriptDir"
    if (Confirm-Action "Remove application files in this directory?" $true) {
        Set-Location $env:TEMP
        Start-DelayedInstallDirRemoval -InstallDir $ScriptDir
    } else {
        Write-Host "Application files kept." -ForegroundColor Yellow
    }
} else {
    Write-Host "Current directory does not look like a FrpManager install folder; skipped app file removal." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Uninstall steps completed." -ForegroundColor Cyan
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
