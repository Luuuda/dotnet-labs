# API Gateway (YARP). Збирати з кореня репозиторію:
#   docker build -f Platform.ApiGateway/Dockerfile -t cinema-gateway .
# Адреси сервісів у контейнері задаються змінними services__<ім'я>__http__0 (так само, як їх передає Aspire).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Platform.ApiGateway/Platform.ApiGateway.csproj Platform.ApiGateway/
RUN dotnet restore Platform.ApiGateway/Platform.ApiGateway.csproj
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.ApiGateway/ Platform.ApiGateway/
RUN dotnet publish Platform.ApiGateway/Platform.ApiGateway.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Platform.ApiGateway.dll"]
