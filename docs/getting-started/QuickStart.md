<div align="center">

# 🚀 Quick Start

</div>

| ⚡ TL;DR |
| -------- |
| Clone the repo, run `docker-compose up -d`, then `dotnet run` the API. Visit `http://localhost:5000/swagger` to explore. |

This guide gets DotNetAtlas running on your local machine in under 5 minutes.

## Prerequisites

Before you begin, ensure you have:

- [**.NET 10 SDK**](https://dotnet.microsoft.com/download) (or later)
- [**Docker Desktop**](https://www.docker.com/products/docker-desktop/) (for infrastructure services)
- **~8GB RAM** available for Docker containers

## 1️⃣ Clone the Repository

```bash
git clone https://github.com/yourusername/DotNetAtlas.git
cd DotNetAtlas
```

## 2️⃣ Start Infrastructure Services

DotNetAtlas requires several infrastructure services: SQL Server, Redis, Kafka, and more. Docker Compose handles all of this:

```bash
docker-compose up -d
```

This starts:
- **SQL Server 2022** - Primary database
- **Redis 7.4** - Caching and SignalR backplane
- **Kafka** (KRaft mode) - Event streaming
- **Schema Registry** - Avro schema management
- **FusionAuth** - Authentication server
- **Jaeger** - Distributed tracing UI
- **Grafana** - Metrics dashboards
- **Seq** - Structured logging UI

Wait for all services to be healthy (~60 seconds on first run):

```bash
docker-compose ps
```

## 3️⃣ Run the API

```bash
dotnet run --project src/DotNetAtlas.Api/DotNetAtlas.Api.csproj
```

The API starts at `http://localhost:5000`.

## 4️⃣ Explore!

| URL | Description |
|-----|-------------|
| http://localhost:5000/swagger | Interactive API documentation |
| http://localhost:5000/health | Health check endpoints |
| http://localhost:5000/signalr-ui | SignalR test interface |
| http://localhost:5000/hangfire-dashboard | Background job dashboard |

### Observability UIs

| URL | Description | Credentials |
|-----|-------------|-------------|
| http://localhost:16686 | Jaeger (Tracing) | - |
| http://localhost:3000 | Grafana (Metrics) | admin/admin |
| http://localhost:5341 | Seq (Logs) | - |
| http://localhost:8080 | AKHQ (Kafka) | - |
| http://localhost:8001 | Redis Insight | - |

## 5️⃣ Try the API

### Get a Weather Forecast

```bash
curl http://localhost:5000/api/v1/weather/forecasts?city=Prague&countryCode=CZ
```

### Submit Feedback (requires authentication)

First, get a JWT token from FusionAuth, then:

```bash
curl -X POST http://localhost:5000/api/v1/weather/feedback \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"text": "Great weather app!", "rating": 5}'
```

## 🎯 What's Next?

Now that you have DotNetAtlas running:

1. **[A Gentle Introduction](AGentleIntroduction.md)** - Understand the architecture
2. **[Step By Step](StepByStep.md)** - Trace a request through the system
3. **[Observability](../features/Observability.md)** - See traces in Jaeger

## 🛠️ Troubleshooting

### Docker containers won't start

Ensure Docker Desktop is running and has sufficient resources allocated (8GB+ RAM recommended).

```bash
docker-compose down -v  # Clean start
docker-compose up -d
```

### Database connection fails

Wait for SQL Server to be fully initialized (~30 seconds). Check logs:

```bash
docker-compose logs sqlserver
```

### Port conflicts

If ports are in use, modify `docker-compose.yaml` or stop conflicting services.

### Still having issues?

Check the [FAQ](../reference/FAQ.md) or [open an issue](https://github.com/yourusername/DotNetAtlas/issues).

