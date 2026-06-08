@echo off
chcp 65001 >nul
title FrpManager 卸载
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
