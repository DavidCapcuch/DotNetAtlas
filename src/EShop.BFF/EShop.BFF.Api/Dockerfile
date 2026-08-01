# syntax=docker/dockerfile:1.26@sha256:ecfaec9ed6d810b56388c508f4121597bfbba70d41a6dfeee4d8cad5f295fc32
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:de3e2d510c3b30dd10a3ababad927725839aacd0bbd6a3e8aef9a5a4408ccc12

FROM ${BASE_IMAGE} AS base
USER $APP_UID
WORKDIR /app
ENV DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace

# Resolve packages into a real image directory, NOT a BuildKit cache mount. `type=gha` exports
# layer filesystem diffs but never cache mounts, so packages left in a mount are re-downloaded on
# every CI run. Writing them into an ordinary layer lets `mode=max` export the restore layer; a
# source-only change is then a cache hit that restores the packages instead of re-fetching them.
ENV NUGET_PACKAGES=/nuget-packages

# NuGet config + tier-isolated CPM files. The BFF lives under src/, so its package versions
# come from src/Directory.Packages.props; the Platform.* libs it references come from
# platform/Directory.Packages.props. (There is no root Directory.Packages.props.)
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Build.targets .
COPY global.json .
COPY platform/Directory.Packages.props platform/
COPY src/Directory.Packages.props src/

# Platform.* csprojs transitively referenced by EShop.BFF.{Api,Infrastructure}
# (Platform.Api → Platform.SharedKernel; Platform.ServiceDefaults → Platform.SharedKernel;
# Platform.SchemaRegistry.Contracts holds the Avro records the bff-group invalidator consumes).
COPY platform/Platform.SharedKernel/Platform.SharedKernel.csproj platform/Platform.SharedKernel/
COPY platform/Platform.ServiceDefaults/Platform.ServiceDefaults.csproj platform/Platform.ServiceDefaults/
COPY platform/Platform.Api/Platform.Api.csproj platform/Platform.Api/
COPY platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj platform/Platform.SchemaRegistry.Contracts/

# BFF project files.
COPY src/EShop.BFF/EShop.BFF.Infrastructure/EShop.BFF.Infrastructure.csproj src/EShop.BFF/EShop.BFF.Infrastructure/
COPY src/EShop.BFF/EShop.BFF.Api/EShop.BFF.Api.csproj src/EShop.BFF/EShop.BFF.Api/

# Lock files must travel with the csproj graph for --locked-mode restore.
COPY platform/Platform.SharedKernel/packages.lock.json platform/Platform.SharedKernel/
COPY platform/Platform.ServiceDefaults/packages.lock.json platform/Platform.ServiceDefaults/
COPY platform/Platform.Api/packages.lock.json platform/Platform.Api/
COPY platform/Platform.SchemaRegistry.Contracts/packages.lock.json platform/Platform.SchemaRegistry.Contracts/
COPY src/EShop.BFF/EShop.BFF.Infrastructure/packages.lock.json src/EShop.BFF/EShop.BFF.Infrastructure/
COPY src/EShop.BFF/EShop.BFF.Api/packages.lock.json src/EShop.BFF/EShop.BFF.Api/

# The single explicit restore, on the csproj-only layer. Publish (FROM build) re-restores implicitly
# against the already-populated /nuget-packages layer — a no-op — so no restore after the source COPY.
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true src/EShop.BFF/EShop.BFF.Api/EShop.BFF.Api.csproj

# Copy the rest of the source.
COPY --link . .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/src/EShop.BFF/EShop.BFF.Api
RUN dotnet publish EShop.BFF.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/DavidCapcuch/DotNetAtlas" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${INFORMATIONAL_VERSION}" \
      org.opencontainers.image.title="EShop.BFF.Api"
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EShop.BFF.Api.dll"]
