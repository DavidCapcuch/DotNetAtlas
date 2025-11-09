FROM mcr.microsoft.com/dotnet/aspnet:10.0.0-rc.1-noble-chiseled-extra@sha256:83795c4b066ec5f0610d8c3fdd977b65e7536c9bbade4f594e80e5fa24fb533a AS base
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1-noble@sha256:eee11b0bf11715710bbe8339b9641f0ef8b5d8a8e07f2d6ff3cd4361c1a4e5a7 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace

# Copy NuGet config and project files to leverage Docker layer cache
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY platform/DotNetAtlas.Outbox.Core/DotNetAtlas.Outbox.Core.csproj platform/DotNetAtlas.Outbox.Core/
COPY platform/DotNetAtlas.Outbox.EntityFrameworkCore/DotNetAtlas.Outbox.EntityFrameworkCore.csproj platform/DotNetAtlas.Outbox.EntityFrameworkCore/
COPY platform/DotNetAtlas.OutboxRelay.WorkerService/DotNetAtlas.OutboxRelay.WorkerService.csproj platform/DotNetAtlas.OutboxRelay.WorkerService/

# Restore with cache mounts
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true platform/DotNetAtlas.OutboxRelay.WorkerService/DotNetAtlas.OutboxRelay.WorkerService.csproj

# Copy the worker service source files
COPY . .
WORKDIR /workspace/platform/DotNetAtlas.OutboxRelay.WorkerService

# Ensure full restore after copying all sources (includes analyzers/source generators)
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true DotNetAtlas.OutboxRelay.WorkerService.csproj

# Build without restore; cache NuGet packages
RUN dotnet build DotNetAtlas.OutboxRelay.WorkerService.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID --no-restore -o /app/build \
    -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/platform/DotNetAtlas.OutboxRelay.WorkerService
RUN dotnet publish DotNetAtlas.OutboxRelay.WorkerService.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DotNetAtlas.OutboxRelay.WorkerService.dll"]
