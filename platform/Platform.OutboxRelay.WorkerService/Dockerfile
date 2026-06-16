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

# Copy NuGet config + tier-isolated CPM files (per CLAUDE.md: platform/).
# OutboxRelay is platform-only — no services-tier dependencies, so services/Directory.Packages.props
# is intentionally NOT copied. The platform-tier Directory.Packages.props supplies every
# PackageReference pinned at that tier (OpenTelemetry.*, Confluent.Kafka, Apache.Avro, etc.).
COPY NuGet.config .
COPY Directory.Build.props .
COPY global.json .
COPY platform/Directory.Packages.props platform/

# Copy every Platform.* csproj transitively referenced by Platform.OutboxRelay.WorkerService.
# Listing each file individually keeps the Docker layer cache valid when unrelated platform libs change.
COPY platform/Platform.SharedKernel/Platform.SharedKernel.csproj platform/Platform.SharedKernel/
COPY platform/Platform.Messaging.Abstractions/Platform.Messaging.Abstractions.csproj platform/Platform.Messaging.Abstractions/
COPY platform/Platform.Avro.UniversalSerDes/Platform.Avro.UniversalSerDes.csproj platform/Platform.Avro.UniversalSerDes/
COPY platform/Platform.ReliableMessaging.Outbox.Core/Platform.ReliableMessaging.Outbox.Core.csproj platform/Platform.ReliableMessaging.Outbox.Core/
COPY platform/Platform.ReliableMessaging.Outbox.EFCore/Platform.ReliableMessaging.Outbox.EFCore.csproj platform/Platform.ReliableMessaging.Outbox.EFCore/
COPY platform/Platform.ServiceDefaults/Platform.ServiceDefaults.csproj platform/Platform.ServiceDefaults/
COPY platform/Platform.OutboxRelay.WorkerService/Platform.OutboxRelay.WorkerService.csproj platform/Platform.OutboxRelay.WorkerService/

# Lock files must travel with the csproj graph for --locked-mode restore.
COPY platform/Platform.SharedKernel/packages.lock.json platform/Platform.SharedKernel/
COPY platform/Platform.Messaging.Abstractions/packages.lock.json platform/Platform.Messaging.Abstractions/
COPY platform/Platform.Avro.UniversalSerDes/packages.lock.json platform/Platform.Avro.UniversalSerDes/
COPY platform/Platform.ReliableMessaging.Outbox.Core/packages.lock.json platform/Platform.ReliableMessaging.Outbox.Core/
COPY platform/Platform.ReliableMessaging.Outbox.EFCore/packages.lock.json platform/Platform.ReliableMessaging.Outbox.EFCore/
COPY platform/Platform.ServiceDefaults/packages.lock.json platform/Platform.ServiceDefaults/
COPY platform/Platform.OutboxRelay.WorkerService/packages.lock.json platform/Platform.OutboxRelay.WorkerService/

# Restore the WorkerService entry-point csproj — pulls every transitive ProjectReference + PackageReference.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true platform/Platform.OutboxRelay.WorkerService/Platform.OutboxRelay.WorkerService.csproj

# Copy the worker service source files
COPY --link . .
WORKDIR /workspace/platform/Platform.OutboxRelay.WorkerService

# Ensure full restore after copying all sources (includes analyzers/source generators)
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true Platform.OutboxRelay.WorkerService.csproj

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/platform/Platform.OutboxRelay.WorkerService
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet publish Platform.OutboxRelay.WorkerService.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/DavidCapcuch/DotNetAtlas" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${INFORMATIONAL_VERSION}" \
      org.opencontainers.image.title="Platform.OutboxRelay.WorkerService"
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Platform.OutboxRelay.WorkerService.dll"]
