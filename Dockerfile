# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY MtgDeckForge.Api/MtgDeckForge.Api.csproj MtgDeckForge.Api/
RUN dotnet restore MtgDeckForge.Api/MtgDeckForge.Api.csproj

# Copy everything and build
COPY . .
WORKDIR /src/MtgDeckForge.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Add curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:5000/healthz || exit 1

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000
ENTRYPOINT ["dotnet", "MtgDeckForge.Api.dll"]
