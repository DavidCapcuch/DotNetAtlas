# syntax=docker/dockerfile:1.7
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:de3e2d510c3b30dd10a3ababad927725839aacd0bbd6a3e8aef9a5a4408ccc12

FROM ${BASE_IMAGE} AS base
USER $APP_UID
WORKDIR /app
ENV DOTNET_gcServer=1 \
    DOTNET_TieredPGO=1
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.100-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace

# NuGet config + tier-isolated CPM files. The BFF lives under src/, so its package versions
# come from src/Directory.Packages.props; the Platform.* libs it references come from
# platform/Directory.Packages.props. (There is no root Directory.Packages.props.)
COPY NuGet.config .
COPY Directory.Build.props .
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

# Restore the API entry-point csproj — pulls every transitive ProjectReference + PackageReference.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true src/EShop.BFF/EShop.BFF.Api/EShop.BFF.Api.csproj

# Copy the rest of the source.
COPY --link . .
WORKDIR /workspace/src/EShop.BFF/EShop.BFF.Api

# Re-restore after the source copy so analyzers/source-generators settle.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true EShop.BFF.Api.csproj

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/src/EShop.BFF/EShop.BFF.Api
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet publish EShop.BFF.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
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
