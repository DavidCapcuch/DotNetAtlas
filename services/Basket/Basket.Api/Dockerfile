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
# Both tier props files MUST be in place before restore so the Basket API project graph
# (Basket.{Domain,Application,Infrastructure,Api} + transitive Platform.* libs) can resolve
# every PackageReference — services-tier packages live in services/Directory.Packages.props,
# platform-tier packages in platform/Directory.Packages.props.
COPY NuGet.config .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY platform/Directory.Packages.props platform/
COPY services/Directory.Packages.props services/

# Copy every Platform.* csproj transitively referenced by Basket.{Api,Infrastructure,Application,Domain}.
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

# Copy Basket project files (project-references already handled above).
COPY services/Basket/Basket.Domain/Basket.Domain.csproj services/Basket/Basket.Domain/
COPY services/Basket/Basket.Application/Basket.Application.csproj services/Basket/Basket.Application/
COPY services/Basket/Basket.Infrastructure/Basket.Infrastructure.csproj services/Basket/Basket.Infrastructure/
COPY services/Basket/Basket.Api/Basket.Api.csproj services/Basket/Basket.Api/

# Lock files must travel with the csproj graph for --locked-mode restore.
COPY services/Basket/Basket.Domain/packages.lock.json services/Basket/Basket.Domain/
COPY services/Basket/Basket.Application/packages.lock.json services/Basket/Basket.Application/
COPY services/Basket/Basket.Infrastructure/packages.lock.json services/Basket/Basket.Infrastructure/
COPY services/Basket/Basket.Api/packages.lock.json services/Basket/Basket.Api/

# Restore the API entry-point csproj — pulls every transitive ProjectReference + PackageReference.
RUN dotnet restore --locked-mode -m:1 -p:PublishReadyToRun=true services/Basket/Basket.Api/Basket.Api.csproj

# Copy the rest of the source.
COPY . .
WORKDIR /workspace/services/Basket/Basket.Api

# Re-restore after the source copy so analyzers/source-generators/avro generators settle.
RUN dotnet restore --locked-mode -m:1 -p:PublishReadyToRun=true Basket.Api.csproj

# Build without restore.
RUN dotnet build Basket.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID --no-restore -m:1 -o /app/build \
    -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0
ARG RUNTIME_ID=linux-x64
WORKDIR /workspace/services/Basket/Basket.Api
RUN dotnet publish Basket.Api.csproj -c $BUILD_CONFIGURATION -r $RUNTIME_ID -m:1 -o /app/publish \
    -p:RestoreLockedMode=true -p:UseAppHost=false -p:PublishReadyToRun=true -p:Version=$VERSION -p:AssemblyVersion=$ASSEMBLY_VERSION -p:FileVersion=$FILE_VERSION -p:InformationalVersion=$INFORMATIONAL_VERSION

FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Basket.Api.dll"]
