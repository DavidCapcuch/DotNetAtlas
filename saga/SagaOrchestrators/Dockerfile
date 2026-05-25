ARG BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.0-noble-chiseled-extra

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

# Copy NuGet config + tier-isolated CPM files (per CLAUDE.md: root + platform/ + saga/).
# Saga lives in the `saga/` tier (alongside services/, platform/, test/), so its CPM file is
# saga/Directory.Packages.props — NOT services/Directory.Packages.props. Both tier props files
# MUST be in place before restore so the SagaOrchestrators project graph + transitive Platform.*
# libs can resolve every PackageReference.
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Packages.props .
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

# Restore the saga entry-point csproj — pulls every transitive ProjectReference + PackageReference.
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true saga/SagaOrchestrators/SagaOrchestrators.csproj

# Copy the rest of the source.
COPY . .
WORKDIR /workspace/saga/SagaOrchestrators

# Re-restore after the source copy so analyzers/source-generators/avro generators settle.
RUN dotnet restore --locked-mode -p:PublishReadyToRun=true SagaOrchestrators.csproj

# Build without restore.
RUN dotnet build SagaOrchestrators.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID --no-restore -o /app/build \
    -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

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
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SagaOrchestrators.dll"]
