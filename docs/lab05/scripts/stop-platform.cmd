@echo off
rem Зупиняє AppHost і всі процеси платформи (контейнери баз Persistent і лишаються працювати)
for %%p in (Platform.AppHost Platform.ApiGateway Platform.Aggregator BookingService.Api CatalogService.Api Reviews.WebApi dcp dcpctrl) do taskkill /IM %%p.exe /T /F >nul 2>&1
