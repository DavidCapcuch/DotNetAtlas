<div align="center">

# 🧅 Clean Architecture

</div>

| ⚡ TL;DR |
| -------- |
| Clean Architecture organizes code into layers where dependencies point inward. Domain has no external dependencies. Infrastructure implements interfaces defined by inner layers. This keeps business logic isolated and testable. |

Clean Architecture, popularized by Robert C. Martin ("Uncle Bob"), is an architectural pattern that separates concerns into concentric layers. The fundamental rule: **dependencies point inward**. Inner layers know nothing about outer layers.

## 🎯 Why Clean Architecture?

Without intentional architecture, codebases naturally drift toward a "Big Ball of Mud" where everything depends on everything. Clean Architecture prevents this by enforcing clear boundaries:

| Problem | Solution |
|---------|----------|
| Business logic coupled to database | Domain layer has no database dependencies |
| Can't test without infrastructure | Interfaces allow mocking/substitution |
| Framework lock-in | Frameworks live in outer layers |
| Scattered business rules | Domain layer centralizes business logic |

## 📦 Project Structure

DotNetAtlas implements Clean Architecture with four projects:

```
src/
├── DotNetAtlas.Domain           # Innermost - Pure business logic
├── DotNetAtlas.Application      # Use cases and orchestration
├── DotNetAtlas.Infrastructure   # External concerns implementation
└── DotNetAtlas.Api              # Entry point - HTTP, SignalR
```

### Dependency Direction

```
┌─────────────────────────────────────────────────────────────┐
│                           Api                                │
│    Depends on: Application, Infrastructure, Domain          │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│                      Infrastructure                          │
│         Depends on: Application, Domain                      │
│         Implements: interfaces from Application             │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│                       Application                            │
│                   Depends on: Domain                         │
│              Defines: interfaces for Infrastructure         │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│                         Domain                               │
│              Depends on: Nothing (only FluentResults)        │
└─────────────────────────────────────────────────────────────┘
```

## 🏛️ Layer Responsibilities

### Domain Layer (`DotNetAtlas.Domain`)

**The heart of the system.** Contains pure business logic with no dependencies on external frameworks.

```
Domain/
├── Alerts/              # AlertSubscriber aggregate
│   ├── AlertSubscriber.cs
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Events/
│   ├── Errors/
│   └── Specifications/
├── Feedback/            # Feedback aggregate
│   ├── Feedback.cs
│   ├── ValueObjects/
│   ├── Events/
│   ├── Errors/
│   └── Specifications/
└── Common/              # Shared domain concepts
```

**What belongs here:**
- Aggregates and entities
- Value objects with validation
- Domain events
- Domain services
- Specifications (query patterns)
- Business rule errors

**What does NOT belong here:**
- Database access code
- HTTP/API concerns
- External service calls
- Framework-specific code

### Application Layer (`DotNetAtlas.Application`)

**Use case orchestration.** Commands, queries, and their handlers. Defines interfaces that infrastructure implements.

```
Application/
├── Common/
│   ├── CQS/              # ICommand, IQuery interfaces
│   ├── Data/             # IDbContext interface
│   └── Validators/       # FluentValidation validators
├── WeatherFeedback/
│   ├── SendFeedback/
│   │   ├── SendFeedbackCommand.cs
│   │   ├── SendFeedbackHandler.cs
│   │   └── SendFeedbackValidator.cs
│   ├── GetFeedback/
│   │   ├── GetFeedbackQuery.cs
│   │   └── GetFeedbackHandler.cs
│   └── ChangeFeedback/
└── WeatherAlerts/
```

**What belongs here:**
- Command and query definitions
- Handlers that orchestrate use cases
- Validation rules (FluentValidation)
- DTOs for input/output
- Interface definitions for infrastructure

**What does NOT belong here:**
- Direct database access (use interfaces)
- HTTP concerns (controllers, endpoints)
- Concrete infrastructure implementations

### Infrastructure Layer (`DotNetAtlas.Infrastructure`)

**External concerns.** Implements interfaces from Application layer. All "I/O" lives here.

```
Infrastructure/
├── Persistence/
│   └── Database/
│       ├── WeatherDbContext.cs
│       ├── EntityConfigurations/
│       ├── Interceptors/
│       └── Migrations/
├── Messaging/
│   ├── Kafka/
│   └── SignalR/
├── HttpClients/
│   └── WeatherProviders/
├── BackgroundJobs/
└── Common/
    ├── Authentication/
    └── Authorization/
```

**What belongs here:**
- EF Core DbContext and configurations
- Kafka producers and consumers
- HTTP client implementations
- External API integrations
- Authentication/authorization
- Background job definitions

### API Layer (`DotNetAtlas.Api`)

**Entry point.** HTTP endpoints, SignalR hubs, middleware. Wires everything together.

```
Api/
├── Program.cs           # Composition root
├── Endpoints/
│   ├── Weather/
│   ├── Admin/
│   └── Auth/
├── SignalRHubs/
├── Common/
│   ├── Middlewares/
│   ├── Exceptions/
│   └── Swagger/
└── Pages/               # SignalR test UI
```

**What belongs here:**
- FastEndpoints endpoint definitions
- SignalR hub definitions
- Middleware (enrichment, exception handling)
- OpenAPI/Swagger configuration
- Dependency injection setup

## 🛡️ Enforcing the Rules

We don't rely on developer discipline alone. **Architecture tests** enforce dependency rules:

```csharp
[Fact]
public void Domain_Should_Not_Depend_On_Application()
{
    var result = Types.InAssembly(DomainAssembly)
        .ShouldNot()
        .HaveDependencyOn("DotNetAtlas.Application")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue();
}

[Fact]
public void Domain_Should_Not_Depend_On_Infrastructure()
{
    var result = Types.InAssembly(DomainAssembly)
        .ShouldNot()
        .HaveDependencyOn("DotNetAtlas.Infrastructure")
        .GetResult();
    
    result.IsSuccessful.Should().BeTrue();
}
```

These tests run in CI. If someone adds a forbidden dependency, the build fails.

Read more: [**Architecture Tests**](../testing/ArchitectureTests.md)

## 🎯 Benefits in Practice

### Testability

Domain logic can be tested without any infrastructure:

```csharp
[Fact]
public void Feedback_Create_RaisesDomainEvent()
{
    // No database, no HTTP, no Kafka - pure domain test
    var text = FeedbackText.Create("Great!").Value;
    var rating = FeedbackRating.Create(5).Value;
    
    var feedback = Feedback.Create(text, rating, userId);
    
    feedback.Value.DomainEvents.Should().ContainSingle()
        .Which.Should().BeOfType<FeedbackCreatedDomainEvent>();
}
```

### Flexibility

Changing the database doesn't affect business logic. Want to switch from SQL Server to PostgreSQL? Only Infrastructure changes.

### Clarity

When you need to understand business rules, look in Domain. When you need to understand a use case, look in Application. The architecture tells you where to find things.

## 📖 Further Reading

- [**Domain-Driven Design**](DomainDrivenDesign.md) - Deep dive into the Domain layer
- [**CQS**](CQS.md) - Command Query Separation in the Application layer
- [**Architecture Tests**](../testing/ArchitectureTests.md) - Enforcing rules automatically

