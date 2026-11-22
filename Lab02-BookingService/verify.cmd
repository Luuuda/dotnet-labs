@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === %date% %time% > verify.log
echo === docker start cinema-mssql >> verify.log
docker start cinema-mssql >> verify.log 2>&1
timeout /t 15 /nobreak >nul
echo === dotnet build >> verify.log
dotnet build BookingService.sln -c Debug -nologo >> verify.log 2>&1
echo BUILD_EXIT=%ERRORLEVEL% >> verify.log
set "CINEMA_TEST_DB=Server=localhost,1433;Database=CinemaBooking;User Id=cinema_booking_app;Password=Booking_App#2026;TrustServerCertificate=True"
echo === dotnet test >> verify.log
dotnet test BookingService.sln --no-build -nologo --logger "console;verbosity=normal" >> verify.log 2>&1
echo TEST_EXIT=%ERRORLEVEL% >> verify.log
echo DONE >> verify.log
