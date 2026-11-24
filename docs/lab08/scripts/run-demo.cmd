@echo off
rem Лаб. 08: демонстрація сценаріїв (платформа має бути запущена: dotnet run --project Platform.AppHost).
rem Потрібні Node.js і puppeteer-core (npm i puppeteer-core), Microsoft Edge, Docker.
chcp 65001 >nul
cd /d "%~dp0"
node demo.js > ..\demo-output.txt 2>&1
