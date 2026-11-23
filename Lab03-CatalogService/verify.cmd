@echo off
rem Lab 03: build, EF Core migration InitialCreate, SQL script, database update, tests.
rem Result: verify.log next to this file.
cd /d "%~dp0"
call :main > verify.log 2>&1
echo Done. See verify.log
exit /b

:main
chcp 65001 >nul
echo === docker ===
docker start cinema-mssql
timeout /t 5 /nobreak >nul

echo === solution ===
if not exist Lab03-CatalogService.sln (
  dotnet new sln -n Lab03-CatalogService --format sln
  dotnet sln Lab03-CatalogService.sln add src\CatalogService.Domain src\CatalogService.Dal src\CatalogService.Bll src\CatalogService.Api src\CatalogService.AppHost tests\CatalogService.Tests
)

echo === dotnet-ef local tool ===
if not exist dotnet-tools.json if not exist .config\dotnet-tools.json dotnet new tool-manifest
dotnet tool install dotnet-ef --version 9.0.*
dotnet tool restore

echo === build ===
dotnet build Lab03-CatalogService.sln -c Debug
if errorlevel 1 (echo BUILD FAILED & exit /b 1)

echo === migration ===
if not exist src\CatalogService.Dal\Migrations (
  dotnet ef migrations add InitialCreate --project src\CatalogService.Dal --startup-project src\CatalogService.Api --output-dir Migrations --no-build
  if errorlevel 1 (echo MIGRATION FAILED & exit /b 1)
  dotnet build Lab03-CatalogService.sln -c Debug
)
dotnet ef migrations list --project src\CatalogService.Dal --startup-project src\CatalogService.Api --no-build

echo === sql scripts ===
dotnet ef migrations script --project src\CatalogService.Dal --startup-project src\CatalogService.Api --no-build -o db\InitialCreate.sql
dotnet ef migrations script --idempotent --project src\CatalogService.Dal --startup-project src\CatalogService.Api --no-build -o db\InitialCreate.idempotent.sql

echo === database update ===
dotnet ef database update --project src\CatalogService.Dal --startup-project src\CatalogService.Api --no-build
if errorlevel 1 (echo DATABASE UPDATE FAILED & exit /b 1)

echo === tests ===
set CATALOG_TEST_DB=Server=localhost,1433;Database=CinemaCatalogEf_Tests;User Id=sa;Password=Cinema_Sa#2026;TrustServerCertificate=True
dotnet test tests\CatalogService.Tests --no-build --logger "console;verbosity=normal"
if errorlevel 1 (echo TESTS FAILED & exit /b 1)
echo ALL OK
exit /b 0
