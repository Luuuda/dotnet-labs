@echo off
rem Лаб. 07: перевірки «Перед здачею» (повтори й circuit breaker, Redis і SQL Server вимкнено, бізнес-метрики) + скріни Dashboard
chcp 65001 >nul
cd /d "%~dp0"
node demo.js
pause
