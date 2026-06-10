@echo off
setlocal
title FrpManager Uninstaller
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
if errorlevel 1 (
    echo.
    echo Uninstall script failed. Press any key to exit.
    pause >nul
)

endlocal
