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
COPY src/DotNetAtlas.Domain/DotNetAtlas.Domain.csproj src/DotNetAtlas.Domain/
COPY src/DotNetAtlas.Infrastructure/DotNetAtlas.Infrastructure.csproj src/DotNetAtlas.Infrastructure/
COPY src/DotNetAtlas.Application/DotNetAtlas.Application.csproj src/DotNetAtlas.Application/
COPY src/DotNetAtlas.Api/DotNetAtlas.Api.csproj src/DotNetAtlas.Api/

# Restore with cache mounts
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore --locked-mode -p:PublishReadyToRun=true src/DotNetAtlas.Api/DotNetAtlas.Api.csproj

# Copy the rest of the source
COPY . .
WORKDIR /workspace/src/DotNetAtlas.Api

# Ensure full restore after copying all sources (includes analyzers/source generators)
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore --locked-mode -p:PublishReadyToRun=true DotNetAtlas.Api.csproj

# Build without restore; cache NuGet packages
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build DotNetAtlas.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID --no-restore -o /app/build \
    -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish DotNetAtlas.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DotNetAtlas.Api.dll"]
