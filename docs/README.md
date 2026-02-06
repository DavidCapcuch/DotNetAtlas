<div align="center">

# 📕 DotNetAtlas Documentation

**A Modern .NET Reference Architecture**

</div>

Welcome to the DotNetAtlas documentation. This guide will help you understand, explore, and learn from a production-ready .NET application that demonstrates Clean Architecture, Domain-Driven Design, and event-driven patterns working together in harmony.

## 🦄 Getting Started

New to DotNetAtlas? Start here:

| Guide | Description |
|-------|-------------|
| [**🚀 Quick Start**](getting-started/QuickStart.md) | Get DotNetAtlas running locally in 5 minutes |
| [**🦄 A Gentle Introduction**](getting-started/AGentleIntroduction.md) | Understand the core concepts and architecture |
| [**👩‍🏫 Step By Step**](getting-started/StepByStep.md) | Complete walkthrough: trace a request from API to database to Kafka |

## 🏛️ Architecture

Understand how the system is designed:

| Topic | Description |
|-------|-------------|
| [**🧅 Clean Architecture**](architecture/CleanArchitecture.md) | Layer organization, dependency rules, and project structure |
| [**📦 Domain-Driven Design**](architecture/DomainDrivenDesign.md) | Aggregates, entities, value objects, and domain events |
| [**🔀 Command Query Separation**](architecture/CQS.md) | Commands, queries, and the decorator pipeline |
| [**📡 Event-Driven Architecture**](architecture/EventDriven.md) | Fire-and-forget vs. transactional outbox patterns |

## 🧩 Platform Libraries

Reusable components you can adapt for your own projects:

| Library | Description |
|---------|-------------|
| [**🧱 SharedKernel**](platform/SharedKernel.md) | DDD building blocks: AggregateRoot, Entity, ValueObject |
| [**⚡ CQS**](platform/CQS.md) | Command/Query handlers with validation, logging, tracing behaviors |
| [**📤 Outbox Pattern**](platform/OutboxPattern.md) | Guaranteed message delivery with transactional outbox |
| [**📥 Inbox Pattern**](platform/InboxPattern.md) | Idempotent message consumption |
| [**🔄 Kafka Middleware**](platform/KafkaMiddleware.md) | Dead letter topics, inbox integration, producer headers |

## ✨ Features

Deep dives into specific capabilities:

| Feature | Description |
|---------|-------------|
| [**💾 Caching**](features/Caching.md) | Multi-level caching with FusionCache and Redis |
| [**🛡️ Resilience**](features/Resilience.md) | Retry, circuit breaker, timeout, and hedging policies |
| [**🔭 Observability**](features/Observability.md) | OpenTelemetry traces, metrics, and structured logging |
| [**🔐 Authentication**](features/Authentication.md) | FusionAuth OIDC integration with JWT validation |
| [**📡 Real-time**](features/SignalR.md) | SignalR hubs with Redis backplane for horizontal scaling |
| [**⏰ Background Jobs**](features/BackgroundJobs.md) | Hangfire for scheduled and recurring tasks |

## 🧪 Testing

Learn our testing strategy:

| Guide | Description |
|-------|-------------|
| [**🧪 Testing Overview**](testing/Overview.md) | Test pyramid, categories, and best practices |
| [**🐳 TestContainers**](testing/TestContainers.md) | Real infrastructure testing with SQL Server, Kafka, Redis |
| [**🏗️ Architecture Tests**](testing/ArchitectureTests.md) | Enforcing Clean Architecture rules with NetArchTest |

## 🚢 DevOps

Production deployment and CI/CD:

| Guide | Description |
|-------|-------------|
| [**🐳 Docker**](devops/Docker.md) | Multi-stage builds, chiseled images, and docker-compose |
| [**🔄 CI/CD Pipeline**](devops/CICD.md) | GitHub Actions workflows for build, test, and deploy |
| [**🗃️ Database Migrations**](devops/Migrations.md) | EF Core migrations and SQL scripts |

## 📖 Reference

| Resource | Description |
|----------|-------------|
| [**📚 Glossary**](reference/Glossary.md) | Key terms and definitions |
| [**🔗 External Resources**](reference/Resources.md) | Books, articles, and talks that inspired this project |
| [**❓ FAQ**](reference/FAQ.md) | Frequently asked questions |

---

## 🎯 Learning Paths

### For Backend Developers
1. [Quick Start](getting-started/QuickStart.md) → Get it running
2. [Clean Architecture](architecture/CleanArchitecture.md) → Understand the layers
3. [DDD](architecture/DomainDrivenDesign.md) → Learn domain modeling
4. [Testing](testing/Overview.md) → Write effective tests

### For Architects
1. [A Gentle Introduction](getting-started/AGentleIntroduction.md) → High-level overview
2. [Event-Driven Architecture](architecture/EventDriven.md) → Messaging patterns
3. [Outbox Pattern](platform/OutboxPattern.md) → Guaranteed delivery
4. [Observability](features/Observability.md) → Production monitoring

### For DevOps Engineers
1. [Docker](devops/Docker.md) → Container setup
2. [CI/CD](devops/CICD.md) → Pipeline configuration
3. [Migrations](devops/Migrations.md) → Database deployment

---

<div align="center">

**DotNetAtlas** is built to be explored. Pick a topic and dive in!

[🏠 GitHub Repository](https://github.com/yourusername/DotNetAtlas) • [📝 Issues](https://github.com/yourusername/DotNetAtlas/issues) • [💬 Discussions](https://github.com/yourusername/DotNetAtlas/discussions)

</div>

