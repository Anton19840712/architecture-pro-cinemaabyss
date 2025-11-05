# Мультистадийная сборка Events Service

# STAGE 1: BUILD
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Копируем .csproj и восстанавливаем зависимости
COPY EventsService/*.csproj ./EventsService/
RUN dotnet restore ./EventsService/EventsService.csproj

# Копируем остальные файлы и собираем проект
COPY EventsService/ ./EventsService/
WORKDIR /app/EventsService
RUN dotnet publish -c Release -o /app/out

# STAGE 2: RUNTIME
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Копируем собранное приложение из build stage
COPY --from=build /app/out .

# Переменные окружения
ENV ASPNETCORE_URLS=http://+:8082
ENV PORT=8082

# Открываем порт
EXPOSE 8082

# Запуск приложения
ENTRYPOINT ["dotnet", "EventsService.dll"]
