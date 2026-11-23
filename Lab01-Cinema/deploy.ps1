<#
  Лаб. 01 — розгортання трьох баз «Кінотеатру» з нуля.

  .\deploy.ps1            — підняти контейнери й виконати schema -> procedures -> seed
  .\deploy.ps1 -Tests     — те саме + запити перевірки (04_tests.sql, 03_tests.sql, 03_queries.js)
  .\deploy.ps1 -Reset     — спершу видалити контейнери РАЗОМ із даними (docker compose down -v)

  Якщо PowerShell не дає запускати скрипти:
      powershell -ExecutionPolicy Bypass -File .\deploy.ps1 -Tests
#>
param(
    [switch]$Tests,
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Set-Location -LiteralPath $PSScriptRoot

$SaPassword = 'Cinema_Sa#2026'   # має збігатися з docker-compose.yml

function Wait-Healthy([string]$Container) {
    Write-Host "Чекаю, поки $Container стане healthy..." -ForegroundColor DarkGray
    for ($i = 0; $i -lt 90; $i++) {
        $state = docker inspect -f '{{.State.Health.Status}}' $Container 2>$null
        if ($state -eq 'healthy') { Write-Host "$Container готовий" -ForegroundColor Green; return }
        Start-Sleep -Seconds 2
    }
    throw "$Container не запустився. Подивіться: docker logs $Container"
}

function Invoke-Sql([string]$File) {
    Write-Host "`n>>> SQL: $File" -ForegroundColor Cyan
    docker exec cinema-mssql /opt/mssql-tools18/bin/sqlcmd `
        -S localhost -U sa -P $SaPassword -C -b -I -f 65001 -i "/scripts/sql/$File"
    if ($LASTEXITCODE -ne 0) { throw "Помилка під час виконання $File" }
}

function Invoke-Mongo([string]$File) {
    Write-Host "`n>>> Mongo: $File" -ForegroundColor Cyan
    docker exec cinema-mongo mongosh --quiet "mongodb://localhost:27017" "/scripts/mongo/$File"
    if ($LASTEXITCODE -ne 0) { throw "Помилка під час виконання $File" }
}

if ($Reset) {
    Write-Host 'Видаляю контейнери й томи з даними...' -ForegroundColor Yellow
    docker compose down -v
}

docker compose up -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose up не вдався. Чи запущено Docker Desktop?' }
Wait-Healthy 'cinema-mssql'
Wait-Healthy 'cinema-mongo'

# 0) бази та логіни
Invoke-Sql '00_init.sql'

# Проєкт №1 — CinemaBooking: schema -> procedures -> seed
Invoke-Sql 'booking/01_schema.sql'
Invoke-Sql 'booking/02_procedures.sql'
Invoke-Sql 'booking/03_seed.sql'

# Проєкт №2 — CinemaCatalog: schema -> seed
Invoke-Sql 'catalog/01_schema.sql'
Invoke-Sql 'catalog/02_seed.sql'

# Проєкт №3 — MongoDB cinema_reviews: колекції/валідація/індекси -> seed -> міграції
Invoke-Mongo '01_schema.js'
Invoke-Mongo '02_seed.js'
Invoke-Mongo 'migrations/001_reviews_add_locale.js'

if ($Tests) {
    Invoke-Sql 'booking/04_tests.sql'
    Invoke-Sql 'catalog/03_tests.sql'
    Invoke-Mongo '03_queries.js'
}

Write-Host "`nГотово. Підключення:" -ForegroundColor Green
Write-Host '  SQL Server : localhost,1433  sa / Cinema_Sa#2026'
Write-Host '               cinema_booking_app / Booking_App#2026 (лише CinemaBooking)'
Write-Host '               cinema_catalog_app / Catalog_App#2026 (лише CinemaCatalog)'
Write-Host '  MongoDB    : mongodb://localhost:27017/cinema_reviews'
