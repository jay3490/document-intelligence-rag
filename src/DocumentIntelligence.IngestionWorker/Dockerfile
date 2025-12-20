# Build context: repository root (docker build -f src/DocumentIntelligence.IngestionWorker/Dockerfile .)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Restore first so the package layer is reused until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DocumentIntelligence.Core/DocumentIntelligence.Core.csproj src/DocumentIntelligence.Core/
COPY src/DocumentIntelligence.Infrastructure/DocumentIntelligence.Infrastructure.csproj src/DocumentIntelligence.Infrastructure/
COPY src/DocumentIntelligence.IngestionWorker/DocumentIntelligence.IngestionWorker.csproj src/DocumentIntelligence.IngestionWorker/
RUN dotnet restore src/DocumentIntelligence.IngestionWorker/DocumentIntelligence.IngestionWorker.csproj

COPY src/ src/
RUN dotnet publish src/DocumentIntelligence.IngestionWorker/DocumentIntelligence.IngestionWorker.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "DocumentIntelligence.IngestionWorker.dll"]
