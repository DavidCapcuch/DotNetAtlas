# syntax=docker/dockerfile:1.7
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:f9bd6be9b5ab75b8196bff0f0972580edaea7fa8ca04e6ef530950e33caee5b0

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

# Resolve packages into a real image directory, NOT a BuildKit cache mount. `type=gha` exports
# layer filesystem diffs but never cache mounts, so packages left in a mount are re-downloaded on
# every CI run. Writing them into an ordinary layer lets `mode=max` export the restore layer; a
# source-only change is then a cache hit that restores the packages instead of re-fetching them.
ENV NUGET_PACKAGES=/nuget-packages

# Copy NuGet config + tier-isolated CPM files (per CLAUDE.md: platform/ + services/).
# Both tier props files MUST be in place before restore so the Inventory API project graph
# (Inventory.{Domain,Application,Infrastructure,API} + transitive Platform.* libs) can resolve
# every PackageReference — services-tier packages live in services/Directory.Packages.props,
# platform-tier packages in platform/Directory.Packages.props.
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Build.targets .
COPY global.json .
COPY platform/Directory.Packages.props platform/
COPY services/Directory.Packages.props services/

# Copy every Platform.* csproj transitively referenced by Inventory.{API,Infrastructure,Application,Domain}.
# Listing each file individually keeps the Docker layer cache valid when unrelated platform libs change.
COPY platform/Platform.SharedKernel/Platform.SharedKernel.csproj platform/Platform.SharedKernel/
COPY platform/Platform.CQRS/Platform.CQRS.csproj platform/Platform.CQRS/
COPY platform/Platform.Messaging.Abstractions/Platform.Messaging.Abstractions.csproj platform/Platform.Messaging.Abstractions/
COPY platform/Platform.Avro.UniversalSerDes/Platform.Avro.UniversalSerDes.csproj platform/Platform.Avro.UniversalSerDes/
COPY platform/Platform.SchemaRegistry.Contracts/Platform.SchemaRegistry.Contracts.csproj platform/Platform.SchemaRegistry.Contracts/
COPY platform/Platform.ReliableMessaging.Inbox.Core/Platform.ReliableMessaging.Inbox.Core.csproj platform/Platform.ReliableMessaging.Inbox.Core/
COPY platform/Platform.ReliableMessaging.Inbox.EFCore/Platform.ReliableMessaging.Inbox.EFCore.csproj platform/Platform.ReliableMessaging.Inbox.EFCore/
COPY platform/Platform.ReliableMessaging.Outbox.Core/Platform.ReliableMessaging.Outbox.Core.csproj platform/Platform.ReliableMessaging.Outbox.Core/
COPY platform/Platform.ReliableMessaging.Outbox.EFCore/Platform.ReliableMessaging.Outbox.EFCore.csproj platform/Platform.ReliableMessaging.Outbox.EFCore/
COPY platform/Platform.KafkaFlow.DeadLetter/Platform.KafkaFlow.DeadLetter.csproj platform/Platform.KafkaFlow.DeadLetter/
COPY platform/Platform.KafkaFlow.Inbox.EFCore/Platform.KafkaFlow.Inbox.EFCore.csproj platform/Platform.KafkaFlow.Inbox.EFCore/
COPY platform/Platform.KafkaFlow.ProducerHeaders/Platform.KafkaFlow.ProducerHeaders.csproj platform/Platform.KafkaFlow.ProducerHeaders/
COPY platform/Platform.ServiceDefaults/Platform.ServiceDefaults.csproj platform/Platform.ServiceDefaults/
COPY platform/Platform.Api/Platform.Api.csproj platform/Platform.Api/

# Copy Inventory project files (project-references already handled above).
COPY services/Inventory/Inventory.Domain/Inventory.Domain.csproj services/Inventory/Inventory.Domain/
COPY services/Inventory/Inventory.Application/Inventory.Application.csproj services/Inventory/Inventory.Application/
COPY services/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj services/Inventory/Inventory.Infrastructure/
COPY services/Inventory/Inventory.Api/Inventory.Api.csproj services/Inventory/Inventory.Api/

# Lock files must travel with the csproj graph for --locked-mode restore.
COPY platform/Platform.Api/packages.lock.json platform/Platform.Api/
COPY services/Inventory/Inventory.Domain/packages.lock.json services/Inventory/Inventory.Domain/
COPY services/Inventory/Inventory.Application/packages.lock.json services/Inventory/Inventory.Application/
COPY services/Inventory/Inventory.Infrastructure/packages.lock.json services/Inventory/Inventory.Infrastructure/
COPY services/Inventory/Inventory.Api/packages.lock.json services/Inventory/Inventory.Api/

# The single explicit restore, on the csproj-only layer. Publish (FROM build) re-restores implicitly
# against the already-populated /nuget-packages layer — a no-op — so no restore after the source COPY.
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true services/Inventory/Inventory.Api/Inventory.Api.csproj

# Copy the rest of the source.
COPY --link . .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/services/Inventory/Inventory.Api
RUN dotnet publish Inventory.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/DavidCapcuch/DotNetAtlas" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${INFORMATIONAL_VERSION}" \
      org.opencontainers.image.title="Inventory.Api"
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
