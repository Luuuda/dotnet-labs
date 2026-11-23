@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Deploying databases and running tests, output goes to deploy.log ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy.ps1" -Tests > "%~dp0deploy.log" 2>&1
echo Exit code: %ERRORLEVEL% >> "%~dp0deploy.log"
echo Done. See deploy.log
timeout /t 5 >nul
