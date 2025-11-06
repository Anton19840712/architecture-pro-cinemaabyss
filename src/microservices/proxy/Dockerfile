# ==============================================================================
# DOCKERFILE для Proxy Service
# Мультистадийная сборка: build stage + runtime stage
# ==============================================================================

# STAGE 1: BUILD
# Используем SDK образ для сборки приложения
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Копируем .csproj и восстанавливаем зависимости
# НЮАНС: Делаем это отдельным слоем для кэширования Docker
COPY ProxyService/*.csproj ./ProxyService/
RUN dotnet restore ./ProxyService/ProxyService.csproj

# Копируем остальные файлы и собираем проект
COPY ProxyService/ ./ProxyService/
WORKDIR /app/ProxyService
RUN dotnet publish -c Release -o /app/out

# STAGE 2: RUNTIME
# Используем облегчённый runtime образ (без SDK)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Устанавливаем curl для healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Копируем собранное приложение из build stage
COPY --from=build /app/out .

# Переменные окружения
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8000

# Открываем порт
EXPOSE 8000

# Запуск приложения
ENTRYPOINT ["dotnet", "ProxyService.dll"]
