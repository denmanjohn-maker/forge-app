# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY mtg-forge.Api/mtg-forge.Api.csproj mtg-forge.Api/
RUN dotnet restore mtg-forge.Api/mtg-forge.Api.csproj

# Copy everything and build
COPY . .
WORKDIR /src/mtg-forge.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Add curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# PORT is injected by Railway at runtime; fall back to 5000 for local Docker runs
EXPOSE 5000
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:${PORT:-5000}/healthz || exit 1

ENTRYPOINT ["dotnet", "mtg-forge.Api.dll"]
