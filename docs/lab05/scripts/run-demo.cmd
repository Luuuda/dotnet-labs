@echo off
rem Демо-сценарій Лаб. 05: запити через Gateway, скріни Dashboard, часткова відмова (зупиняє reviews-api)
chcp 65001 >nul
cd /d "%~dp0"
node demo.js
pause
