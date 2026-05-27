# syntax=docker/dockerfile:1.7
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

# Copy NuGet config + tier-isolated CPM files (per CLAUDE.md: root + platform/ + services/).
# Both tier props files MUST be in place before restore so the Ordering API project graph
# (Ordering.{Domain,Application,Infrastructure,API} + transitive Platform.* libs) can resolve
# every PackageReference — services-tier packages live in services/Directory.Packages.props,
# platform-tier packages in platform/Directory.Packages.props.
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY platform/Directory.Packages.props platform/
COPY services/Directory.Packages.props services/

# Copy every Platform.* csproj transitively referenced by Ordering.{API,Infrastructure,Application,Domain}.
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

# Copy Ordering project files (project-references already handled above).
COPY services/Ordering/Ordering.Domain/Ordering.Domain.csproj services/Ordering/Ordering.Domain/
COPY services/Ordering/Ordering.Application/Ordering.Application.csproj services/Ordering/Ordering.Application/
COPY services/Ordering/Ordering.Infrastructure/Ordering.Infrastructure.csproj services/Ordering/Ordering.Infrastructure/
COPY services/Ordering/Ordering.Api/Ordering.Api.csproj services/Ordering/Ordering.Api/

# Lock files must travel with the csproj graph for --locked-mode restore.
COPY services/Ordering/Ordering.Domain/packages.lock.json services/Ordering/Ordering.Domain/
COPY services/Ordering/Ordering.Application/packages.lock.json services/Ordering/Ordering.Application/
COPY services/Ordering/Ordering.Infrastructure/packages.lock.json services/Ordering/Ordering.Infrastructure/
COPY services/Ordering/Ordering.Api/packages.lock.json services/Ordering/Ordering.Api/

# Restore the API entry-point csproj — pulls every transitive ProjectReference + PackageReference.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true services/Ordering/Ordering.Api/Ordering.Api.csproj

# Copy the rest of the source.
COPY --link . .
WORKDIR /workspace/services/Ordering/Ordering.Api

# Re-restore after the source copy so analyzers/source-generators/avro generators settle.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet restore --locked-mode -p:PublishReadyToRun=true Ordering.Api.csproj

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/services/Ordering/Ordering.Api
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget dotnet publish Ordering.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
LABEL org.opencontainers.image.source="https://github.com/DavidCapcuch/DotNetAtlas" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${INFORMATIONAL_VERSION}" \
      org.opencontainers.image.title="Ordering.Api"
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ordering.Api.dll"]
