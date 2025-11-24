# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY EventsService/*.csproj ./EventsService/
RUN dotnet restore ./EventsService/EventsService.csproj

# Copy source code and build
COPY EventsService/ ./EventsService/
WORKDIR /src/EventsService
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8082

EXPOSE 8082

ENTRYPOINT ["dotnet", "EventsService.dll"]
