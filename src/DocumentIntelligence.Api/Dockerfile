# Build context: repository root (docker build -f src/DocumentIntelligence.Api/Dockerfile .)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Restore first so the package layer is reused until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DocumentIntelligence.Core/DocumentIntelligence.Core.csproj src/DocumentIntelligence.Core/
COPY src/DocumentIntelligence.Infrastructure/DocumentIntelligence.Infrastructure.csproj src/DocumentIntelligence.Infrastructure/
COPY src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj src/DocumentIntelligence.Api/
RUN dotnet restore src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj

COPY src/ src/
RUN dotnet publish src/DocumentIntelligence.Api/DocumentIntelligence.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Mount point for the shared filesystem blob store; owned by the app user so a fresh
# named volume mounted here is writable.
RUN mkdir -p /data/blobs && chown $APP_UID /data/blobs

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# The aspnet images ship an unprivileged "app" user.
USER $APP_UID
ENTRYPOINT ["dotnet", "DocumentIntelligence.Api.dll"]
