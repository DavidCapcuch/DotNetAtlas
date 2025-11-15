# DotNetAtlas - Product Description

## What Is This Project?

DotNetAtlas serves as a reference architecture for modern .NET applications, designed to bridge the gap between theoretical knowledge and practical implementation of enterprise-grade software systems.

Think of it as a "living textbook" - not just documentation, but a fully functional, runnable codebase that demonstrates best practices in action.

## Why Does This Project Exist?

### Primary Problems It Solves

1. **Knowledge Gap**: Developers often lack real-world examples of how to properly implement Clean Architecture, DDD, and event-driven patterns together
2. **Technology Integration Complexity**: Combining Kafka, Redis, SignalR, EF Core, and observability tools properly is challenging
3. **Testing Strategy Uncertainty**: Many projects struggle with comprehensive testing strategies using real infrastructure
4. **Production Readiness**: Bridging the gap between tutorial code and production-grade implementation
5. **Observability Blind Spots**: Understanding how to implement complete distributed tracing across async operations

## What Makes It Different?

- **Fully Runnable**: Not just code snippets - complete working solution with full infrastructure in docker-compose
- **Real Infrastructure**: Tests use TestContainers with actual SQL Server, Kafka, Redis
- **Trace Continuity**: Complete OpenTelemetry tracing from HTTP request through async Kafka delivery
- **Production Patterns**: Outbox pattern, resilience, caching, all implemented properly
- **Patterns and components** that can be adapted and reused in real projects as needed.

## Who Is This For?

### Software Developers

- **Real-world implementations** of Clean Architecture, DDD, CQS, and event-driven patterns
- **Patterns and components** that can be adapted and reused in real projects as needed.
- **Comprehensive observability** showing distributed system behavior
- **Realistic testing strategies** using actual infrastructure components

### Software Architects

- **A reference architecture** demonstrating common enterprise integrations.
- **Examples of cross‑cutting concerns:** authentication, authorization, logging, resilience, configuration, and observability.
- Patterns for scalability, reliability, and operational readiness.
- **DevOps integration** examples with CI/CD and container orchestration

### Technical Teams

- Understanding modern .NET development practices and establishing coding standards, patterns and project setup
- **Pattern library** for solving common enterprise architecture challenges
- **Implementation examples** for complex integration scenarios

### Use Cases

1. **Learning**: Study how patterns like Outbox, CQS, DDD work together
2. **Reference**: Look up "how do I implement X in .NET?" and find **working code**
3. **Template**: Start new projects with proven architecture
4. **Training**: Onboard team members with **working examples**
5. **Evaluation**: Learn and assess new  technologies and their trade-offs  

## What Does It Demonstrate?

### Core Features

The project demonstrates its capabilities through a simple **weather-related domain** for learning:

- **Forecasts**: Get weather forecasts with caching, resilience patterns
- **Feedback**: Submit feedback with guaranteed event delivery (Outbox pattern)
- **Real-time alerts**: via SignalR with Redis backplane
- Complete observability with OpenTelemetry

### Key Demonstrations

1. **Event-Driven Architecture**
   - Fire-and-forget events (forecast requests → Kafka immediately)
   - Transactional outbox (feedback events → database → worker → Kafka)
   - Complete trace continuity across async boundaries

2. **Domain-Driven Design**
   - Aggregates with domain events (Feedback aggregate)
   - Value Objects with validation (FeedbackText, FeedbackRating)
   - Domain-centric error handling (Result pattern)

3. **Testing Strategy**
   - Unit tests for domain logic
   - Integration tests with real infrastructure (TestContainers)
   - Functional tests for complete API flows
   - Architecture tests validating Clean Architecture rules

4. **Resilience & Performance**
   - HTTP client retry, circuit breaker, timeout policies
   - Hedging strategy racing multiple providers
   - L1/L2 caching with Redis backplane
   - Fail-safe serving stale data when sources fail

5. **Real-Time Communication**
   - SignalR hubs with JWT authentication
   - Redis backplane for horizontal scaling
   - Custom Redis group management using Lua scripts
   - Background jobs triggered by group membership

6. **Production Operations**
   - Complete observability (Jaeger, Grafana, Prometheus, Seq)
   - Health checks for all dependencies
   - Grafana dashboards pre-configured
   - Docker Compose for local development
   - CI/CD with GitHub Actions

## How Should Users Explore This?

### Recommended Learning Path

1. **Start with Brief**: Read `.kilocode/rules/memory-bank/brief.md`
2. **Run Locally**: `docker-compose up` → `dotnet run src/DotNetAtlas.Api/DotNetAtlas.Api.csproj` explore at http://localhost:5000
3. **Study Domain**: Examine `Feedback` aggregate in Domain project
4. **Trace a Request**: Send feedback → watch in Jaeger → see Kafka message
5. **Explore Tests**: See how TestContainers enable real infrastructure testing
6. **Deep Dive**: Pick a specific pattern (Outbox, SignalR, Caching) and study implementation

### Key Entry Points

- **API**: `/swagger` - Interactive API documentation
- **Tracing**: Jaeger UI - See distributed traces
- **Metrics**: Grafana dashboards - Monitor system health
- **SignalR**: `/signalr-ui` - Test real-time functionality
- **Tests**: Run tests, watch Jaeger show test traces

## Project Philosophy

### Design Principles

1. **Pragmatic over Dogmatic**: Use patterns where they add value, not everywhere
2. **Production-Ready**: Code quality suitable for production use
3. **Real Infrastructure**: Test with actual dependencies, not mocks
4. **Developer Experience**: Easy to run, explore, and debug
5. **Modern .NET**: Leverage latest .NET features and libraries

### What This Is NOT

- ❌ Not a microservices example (single monolith showing patterns)
- ❌ Not a "hello world" tutorial (production-grade complexity)
- ❌ Not opinionated about every choice (shows options like fire-and-forget vs outbox)
- ❌ Not a framework or library (reference implementation)

## Success Criteria

**Users should be able to:**

- Understand **Clean Architecture** boundaries and dependencies
- Implement **Domain-Driven Design** with proper aggregates and entities
- Use **Event-Driven Architecture** for system communication
- Apply **Command Query Separation** for clear responsibility separation
- Integrate **Observability** for system monitoring and debugging
- Implement **Resilience patterns** for fault tolerance
- Understand **trade-offs** between different approaches

## Business Value Delivered

### Technical Excellence

- **Maintainable code** through clear separation of concerns
- **Testable architecture** enabling confidence in changes
- **Observable systems** with comprehensive monitoring and tracing
- **Scalable design** supporting horizontal scaling patterns

### Development Efficiency

- **Pattern reuse** reducing time spent on architectural decisions
- **Best practices** preventing common pitfalls and anti-patterns
- **Tool integration** showing how different technologies work together
- **Documentation** providing clear implementation guidance
