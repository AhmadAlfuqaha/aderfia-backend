# =============================================================================
# Aderfia API — production image for Render
#
# Multi-stage: the SDK (~800MB) builds, the ASP.NET runtime (~220MB) ships.
# Nothing in here is a secret. Every environment-specific value — connection
# string, admin key, CORS origins, media URL — is read from the environment at
# RUNTIME and set in Render's dashboard. See .env.production.example.
# =============================================================================

# ---- Stage 1: build & publish ----------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Project manifests first, on their own layer. Restore is the slow step, and
# it only depends on these files — so editing C# re-uses the cached packages
# instead of re-downloading NuGet on every build.
#
# Directory.Build.props and Directory.Packages.props are NOT optional here:
# central package management means the .csproj files carry no versions, and
# restore fails without them.
COPY Directory.Build.props Directory.Packages.props Aderfia.sln ./
COPY src/Aderfia.Domain/Aderfia.Domain.csproj                               src/Aderfia.Domain/
COPY src/Aderfia.Application/Aderfia.Application.csproj                     src/Aderfia.Application/
COPY src/Aderfia.Infrastructure/Aderfia.Infrastructure.csproj               src/Aderfia.Infrastructure/
COPY src/Aderfia.Persistence/Aderfia.Persistence.csproj                     src/Aderfia.Persistence/
COPY src/Aderfia.Persistence.SqlServer/Aderfia.Persistence.SqlServer.csproj src/Aderfia.Persistence.SqlServer/
COPY src/Aderfia.Api/Aderfia.Api.csproj                                     src/Aderfia.Api/

RUN dotnet restore Aderfia.sln

COPY . .

# Publishing the API pulls in every project it references, which includes
# Aderfia.Persistence.SqlServer — the assembly holding the SQL Server
# migrations. Without it in the image, UseSqlServer's MigrationsAssembly
# cannot be loaded and the app fails at startup.
RUN dotnet publish src/Aderfia.Api/Aderfia.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- Stage 2: runtime ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Defaults, all overridable by Render's environment variables. Kestrel reads
# ASPNETCORE_URLS; it does NOT read a bare PORT, so this has to be explicit.
# 0.0.0.0 rather than localhost, or nothing outside the container can reach it.
ENV ASPNETCORE_URLS=http://0.0.0.0:10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

# The image runs as a non-root user (APP_UID is 1654 in the .NET 8+ images).
# wwwroot has to be writable anyway: AddInfrastructure calls
# Directory.CreateDirectory on wwwroot/media at STARTUP, so a read-only tree
# would crash the app before it served a request — not merely break uploads.
#
# Render's filesystem is ephemeral: uploads work for the life of the instance
# and are lost on redeploy. Attach a disk or move to Blob storage to keep them.
RUN mkdir -p /app/wwwroot/media && chown -R $APP_UID /app/wwwroot
USER $APP_UID

EXPOSE 10000

ENTRYPOINT ["dotnet", "Aderfia.Api.dll"]
