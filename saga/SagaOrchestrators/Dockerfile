# syntax=docker/dockerfile:1.7
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:f5b3b2e2e548828d50e349726f51a5de001286f02c4bbde77db0dd34eb9f55ff

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

# Copy NuGet config + tier-isolated CPM files (per CLAUDE.md: platform/ + saga/).
# Saga lives in the `saga/` tier (alongside services/, platform/, test/), so its CPM file is
# saga/Directory.Packages.props — NOT services/Directory.Packages.props. Both tier props files
# MUST be in place before restore so the SagaOrchestrators project graph + transitive Platform.*
# libs can resolve every PackageReference.
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Build.targets .
COPY global.json .
COPY platform/Directory.Packages.props platform/
COPY saga/Directory.Packages.props saga/

# Copy every Platform.* csproj transitively referenced by SagaOrchestrators.
# Saga's direct refs are Platform.{Avro.UniversalSerDes,ServiceDefaults,ReliableMessaging.Outbox.{Core,EFCore},
# SchemaRegistry.Contracts,SharedKernel}. The rest (Inbox.*, KafkaFlow.*, CQRS, Messaging.Abstractions)
# are listed for layer-cache uniformity with sibling BC API Dockerfiles — restore against the saga
# csproj graph won't try to resolve them.
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

# Copy Saga project files (project-references already handled above). Saga has a flat layout
# (no Application/Infrastructure split) — only the single SagaOrchestrators csproj.
COPY saga/SagaOrchestrators/SagaOrchestrators.csproj saga/SagaOrchestrators/

# Lock file must travel with the csproj graph for --locked-mode restore.
COPY saga/SagaOrchestrators/packages.lock.json saga/SagaOrchestrators/

# The single explicit restore, on the csproj-only layer. Publish (FROM build) re-restores implicitly
# against the already-populated /nuget-packages layer — a no-op — so no restore after the source COPY.
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true saga/SagaOrchestrators/SagaOrchestrators.csproj

# Copy the rest of the source.
COPY --link . .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/saga/SagaOrchestrators
RUN dotnet publish SagaOrchestrators.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/DavidCapcuch/DotNetAtlas" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${INFORMATIONAL_VERSION}" \
      org.opencontainers.image.title="SagaOrchestrators"
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SagaOrchestrators.dll"]
