@echo off
rem Запуск усієї платформи через Aspire AppHost (Dashboard: http://localhost:15500, Gateway: http://localhost:5000)
chcp 65001 >nul
cd /d "%~dp0..\..\.."
dotnet run --project Platform.AppHost --launch-profile http
