<div align="center">

# 🐳 Docker

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses Docker Compose to run all infrastructure (SQL Server, Redis, Kafka, FusionAuth, Jaeger, Seq, Grafana). Multi-stage Dockerfiles create optimized production images. `docker compose up -d` starts everything. |

Docker provides consistent environments from development to production. DotNetAtlas includes a complete Docker Compose setup for local development and production-ready Dockerfiles.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Docker Compose                            │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Application                           ││
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     ││
│  │  │     API     │  │   Worker    │  │   Outbox    │     ││
│  │  │   :5000     │  │   Relay     │  │   Worker    │     ││
│  │  └─────────────┘  └─────────────┘  └─────────────┘     ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Infrastructure                        ││
│  │  ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐    ││
│  │  │  SQL  │ │ Redis │ │ Kafka │ │Fusion │ │Zook-  │    ││
│  │  │Server │ │       │ │       │ │ Auth  │ │keeper │    ││
│  │  │:1433  │ │:6379  │ │:9092  │ │:9011  │ │:2181  │    ││
│  │  └───────┘ └───────┘ └───────┘ └───────┘ └───────┘    ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Observability                         ││
│  │  ┌───────┐ ┌───────┐ ┌───────┐ ┌───────┐              ││
│  │  │Jaeger │ │  Seq  │ │Grafana│ │Prome- │              ││
│  │  │:16686 │ │:5341  │ │:3000  │ │theus  │              ││
│  │  └───────┘ └───────┘ └───────┘ └───────┘              ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 📦 Docker Compose

### docker-compose.yml

```yaml
services:
  # Database
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "YourStrong!Passw0rd"
    ports:
      - "1433:1433"
    volumes:
      - sqlserver-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -Q "SELECT 1" -C
      interval: 10s
      timeout: 5s
      retries: 5

  # Cache
  redis:
    image: redis:7.4-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    healthcheck:
      test: redis-cli ping
      interval: 10s
      timeout: 5s
      retries: 5

  # Message Broker
  zookeeper:
    image: confluentinc/cp-zookeeper:7.5.0
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
    volumes:
      - zookeeper-data:/var/lib/zookeeper/data

  kafka:
    image: confluentinc/cp-kafka:7.5.0
    depends_on:
      - zookeeper
    ports:
      - "9092:9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
    volumes:
      - kafka-data:/var/lib/kafka/data
    healthcheck:
      test: kafka-broker-api-versions --bootstrap-server localhost:9092
      interval: 10s
      timeout: 5s
      retries: 5

  # Identity
  fusionauth:
    image: fusionauth/fusionauth-app:latest
    depends_on:
      - sqlserver
    ports:
      - "9011:9011"
    environment:
      DATABASE_URL: jdbc:sqlserver://sqlserver:1433;databaseName=fusionauth
      DATABASE_USERNAME: sa
      DATABASE_PASSWORD: "YourStrong!Passw0rd"
      FUSIONAUTH_APP_MEMORY: 512M

  # Observability
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"  # UI
      - "4317:4317"    # OTLP gRPC
      - "4318:4318"    # OTLP HTTP

  seq:
    image: datalust/seq:latest
    ports:
      - "5341:80"
    environment:
      ACCEPT_EULA: "Y"
    volumes:
      - seq-data:/data

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
      - prometheus-data:/prometheus

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    volumes:
      - grafana-data:/var/lib/grafana
    environment:
      GF_SECURITY_ADMIN_PASSWORD: admin

volumes:
  sqlserver-data:
  redis-data:
  zookeeper-data:
  kafka-data:
  seq-data:
  prometheus-data:
  grafana-data:
```

## 🏭 Multi-Stage Dockerfile

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["src/DotNetAtlas.Api/DotNetAtlas.Api.csproj", "DotNetAtlas.Api/"]
COPY ["src/DotNetAtlas.Application/DotNetAtlas.Application.csproj", "DotNetAtlas.Application/"]
COPY ["src/DotNetAtlas.Domain/DotNetAtlas.Domain.csproj", "DotNetAtlas.Domain/"]
COPY ["src/DotNetAtlas.Infrastructure/DotNetAtlas.Infrastructure.csproj", "DotNetAtlas.Infrastructure/"]
RUN dotnet restore "DotNetAtlas.Api/DotNetAtlas.Api.csproj"

# Copy source and build
COPY src/ .
RUN dotnet build "DotNetAtlas.Api/DotNetAtlas.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DotNetAtlas.Api/DotNetAtlas.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080

# Create non-root user
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DotNetAtlas.Api.dll"]
```

## 🚀 Commands

```bash
# Start all services
docker compose up -d

# Start specific services
docker compose up -d sqlserver redis kafka

# View logs
docker compose logs -f api

# Stop all services
docker compose down

# Stop and remove volumes
docker compose down -v

# Rebuild images
docker compose build --no-cache

# Scale services
docker compose up -d --scale api=3
```

## 🔧 Development vs Production

### Development

```yaml
# docker-compose.override.yml (auto-loaded)
services:
  api:
    build:
      context: .
      dockerfile: src/DotNetAtlas.Api/Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Development
    volumes:
      - ./src:/src:ro  # Hot reload
    ports:
      - "5000:8080"
```

### Production

```yaml
# docker-compose.prod.yml
services:
  api:
    image: dotnetatlas/api:${TAG:-latest}
    environment:
      ASPNETCORE_ENVIRONMENT: Production
    deploy:
      replicas: 3
      resources:
        limits:
          cpus: '1'
          memory: 1G
```

## 📖 Further Reading

- [**Quick Start**](../getting-started/QuickStart.md) - Getting started with Docker
- [**CI/CD**](CICD.md) - Building images in pipelines

