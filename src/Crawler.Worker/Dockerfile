# Build context is the repository root (see docker-compose.yml).

# --- Stage 1: compile with the full SDK ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first: the restore layer is cached until a dependency changes.
COPY src/Crawler.Domain/Crawler.Domain.csproj src/Crawler.Domain/
COPY src/Crawler.Infrastructure/Crawler.Infrastructure.csproj src/Crawler.Infrastructure/
COPY src/Crawler.Worker/Crawler.Worker.csproj src/Crawler.Worker/
RUN dotnet restore src/Crawler.Worker/Crawler.Worker.csproj

COPY src/ src/
RUN dotnet publish src/Crawler.Worker/Crawler.Worker.csproj -c Release -o /app --no-restore

# --- Stage 2: small runtime image (no SDK, no source) ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Crawler.Worker.dll"]
