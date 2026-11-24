# Catalog API (EF Core). Збирати з кореня репозиторію:
#   docker build -f Services/Catalog/CatalogService.Api/Dockerfile -t cinema-catalog-api .
# Рядок підключення: змінна ConnectionStrings__CatalogDb; RabbitMQ (Лаб. 08) — ConnectionStrings__messaging (без неї події лише логуються).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Platform.ServiceDefaults/Platform.ServiceDefaults.csproj Platform.ServiceDefaults/
COPY Platform.EventBus/Platform.EventBus.csproj Platform.EventBus/
COPY Services/Catalog/CatalogService.Domain/CatalogService.Domain.csproj Services/Catalog/CatalogService.Domain/
COPY Services/Catalog/CatalogService.Dal/CatalogService.Dal.csproj Services/Catalog/CatalogService.Dal/
COPY Services/Catalog/CatalogService.Bll/CatalogService.Bll.csproj Services/Catalog/CatalogService.Bll/
COPY Services/Catalog/CatalogService.Api/CatalogService.Api.csproj Services/Catalog/CatalogService.Api/
RUN dotnet restore Services/Catalog/CatalogService.Api/CatalogService.Api.csproj
COPY Platform.ServiceDefaults/ Platform.ServiceDefaults/
COPY Platform.EventBus/ Platform.EventBus/
COPY Services/Catalog/ Services/Catalog/
RUN dotnet publish Services/Catalog/CatalogService.Api/CatalogService.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CatalogService.Api.dll"]
