FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["services/Notifications/Notifications.Api/Notifications.Api.csproj", "services/Notifications/Notifications.Api/"]
COPY ["services/Notifications/Notifications.Application/Notifications.Application.csproj", "services/Notifications/Notifications.Application/"]
COPY ["services/Notifications/Notifications.Domain/Notifications.Domain.csproj", "services/Notifications/Notifications.Domain/"]
COPY ["services/Notifications/Notifications.Infrastructure/Notifications.Infrastructure.csproj", "services/Notifications/Notifications.Infrastructure/"]
RUN dotnet restore "services/Notifications/Notifications.Api/Notifications.Api.csproj"
COPY . .
WORKDIR "/src/services/Notifications/Notifications.Api"
RUN dotnet build "./Notifications.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Notifications.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notifications.Api.dll"]
