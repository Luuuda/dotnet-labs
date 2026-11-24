@echo off
rem Лаб. 06: перевірки «Перед здачею» (кеш, інвалідація, Redis вимкнено, gRPC, часткова відповідь) + скріни Dashboard
chcp 65001 >nul
cd /d "%~dp0"
node demo.js
pause
