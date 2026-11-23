@echo off
rem Lab 04: build + tests against MongoDB (container cinema-mongo from Lab 01). Result: verify.log
cd /d "%~dp0"
call :main > verify.log 2>&1
echo Done. See verify.log
exit /b

:main
chcp 65001 >nul
echo === docker ===
docker start cinema-mongo
timeout /t 3 /nobreak >nul

echo === solution ===
if not exist Lab04-ReviewsService.sln (
  dotnet new sln -n Lab04-ReviewsService --format sln
  dotnet sln Lab04-ReviewsService.sln add src\Reviews.Domain src\Reviews.Application src\Reviews.Infrastructure src\Reviews.WebApi tests\Reviews.Tests
)

echo === build ===
dotnet build Lab04-ReviewsService.sln -c Debug
if errorlevel 1 (echo BUILD FAILED & exit /b 1)

echo === tests ===
set REVIEWS_TEST_MONGO=mongodb://localhost:27017/?serverSelectionTimeoutMS=5000
dotnet test tests\Reviews.Tests --no-build --logger "console;verbosity=normal"
if errorlevel 1 (echo TESTS FAILED & exit /b 1)
echo ALL OK
exit /b 0
