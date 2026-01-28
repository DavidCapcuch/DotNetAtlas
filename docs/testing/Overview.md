<div align="center">

# 🧪 Testing Overview

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses a test pyramid: many unit tests (fast, isolated), fewer integration tests (real infrastructure via TestContainers), and architecture tests (enforce rules). Tests use xUnit, FluentAssertions, and NSubstitute. |

Testing is essential for confidence in your code. DotNetAtlas demonstrates a comprehensive testing strategy that balances speed, coverage, and realism.

## 🏗️ Test Pyramid

```
                    ┌───────────┐
                    │   E2E     │  Few, slow, high confidence
                    │  Tests    │
                    └─────┬─────┘
                          │
                ┌─────────┴─────────┐
                │   Integration     │  Some, medium speed
                │      Tests        │  Real infrastructure
                └─────────┬─────────┘
                          │
        ┌─────────────────┴─────────────────┐
        │           Unit Tests              │  Many, fast, isolated
        │     Domain • Handlers • Logic     │
        └───────────────────────────────────┘
```

## 📦 Test Projects

```
tests/
├── DotNetAtlas.Domain.UnitTests/           # Domain logic tests
├── DotNetAtlas.Application.UnitTests/      # Handler tests
├── DotNetAtlas.Infrastructure.UnitTests/   # Infrastructure tests
├── DotNetAtlas.Api.IntegrationTests/       # API integration tests
└── DotNetAtlas.ArchitectureTests/          # Architecture rule tests
```

## 🧱 Unit Tests

Unit tests verify individual components in isolation. Dependencies are mocked.

### Domain Tests

Test aggregates, entities, and value objects without any infrastructure:

```csharp
public class FeedbackTests
{
    [Fact]
    public void Create_WithValidInput_ReturnsFeedback()
    {
        // Arrange
        var text = FeedbackText.Create("Great service!").Value;
        var rating = FeedbackRating.Create(5).Value;
        var userId = Guid.CreateVersion7();
        
        // Act
        var result = Feedback.Create(text, rating, userId);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.FeedbackText.Should().Be(text);
        result.Value.Rating.Should().Be(rating);
    }
    
    [Fact]
    public void Create_RaisesFeedbackCreatedDomainEvent()
    {
        // Arrange
        var text = FeedbackText.Create("Great!").Value;
        var rating = FeedbackRating.Create(5).Value;
        
        // Act
        var feedback = Feedback.Create(text, rating, Guid.CreateVersion7()).Value;
        var events = feedback.PopDomainEvents();
        
        // Assert
        events.Should().ContainSingle()
            .Which.Should().BeOfType<FeedbackCreatedDomainEvent>();
    }
}
```

### Value Object Tests

```csharp
public class FeedbackRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_WithValidRating_ReturnsSuccess(int value)
    {
        var result = FeedbackRating.Create(value);
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }
    
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Create_WithInvalidRating_ReturnsFailure(int value)
    {
        var result = FeedbackRating.Create(value);
        
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => 
            e.Message.Contains("between 1 and 5"));
    }
    
    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var rating1 = FeedbackRating.Create(5).Value;
        var rating2 = FeedbackRating.Create(5).Value;
        
        rating1.Should().Be(rating2);
        (rating1 == rating2).Should().BeTrue();
    }
}
```

### Handler Tests

Test handlers with mocked dependencies:

```csharp
public class SendFeedbackHandlerTests
{
    private readonly IWeatherDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly SendFeedbackHandler _handler;
    
    public SendFeedbackHandlerTests()
    {
        _dbContext = Substitute.For<IWeatherDbContext>();
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Id.Returns(Guid.CreateVersion7());
        
        _handler = new SendFeedbackHandler(_dbContext, _currentUser);
    }
    
    [Fact]
    public async Task Handle_WithValidCommand_CreatesFeedback()
    {
        // Arrange
        var command = new SendFeedbackCommand("Great!", 5);
        var feedbackSet = new List<Feedback>();
        _dbContext.Feedback.Returns(feedbackSet.AsQueryable().BuildMockDbSet());
        
        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
```

## 🔌 Integration Tests

Integration tests verify components work together with real infrastructure.

### TestContainers Setup

```csharp
public class IntegrationTestFixture : IAsyncLifetime
{
    public MsSqlContainer SqlServer { get; private set; } = null!;
    public RedisContainer Redis { get; private set; } = null!;
    public KafkaContainer Kafka { get; private set; } = null!;
    
    public async Task InitializeAsync()
    {
        SqlServer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        
        Redis = new RedisBuilder()
            .WithImage("redis:7.4-alpine")
            .Build();
        
        Kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.5.0")
            .Build();
        
        await Task.WhenAll(
            SqlServer.StartAsync(),
            Redis.StartAsync(),
            Kafka.StartAsync());
    }
    
    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            SqlServer.DisposeAsync().AsTask(),
            Redis.DisposeAsync().AsTask(),
            Kafka.DisposeAsync().AsTask());
    }
}
```

Read more: [**TestContainers**](TestContainers.md)

## 🏛️ Architecture Tests

Architecture tests enforce Clean Architecture rules:

```csharp
public class ArchitectureTests
{
    [Fact]
    public void Domain_Should_Not_Depend_On_Application()
    {
        var result = Types.InAssembly(typeof(Feedback).Assembly)
            .ShouldNot()
            .HaveDependencyOn("DotNetAtlas.Application")
            .GetResult();
        
        result.IsSuccessful.Should().BeTrue();
    }
}
```

Read more: [**Architecture Tests**](ArchitectureTests.md)

## 🛠️ Testing Tools

| Tool | Purpose |
|------|---------|
| **xUnit** | Test framework |
| **FluentAssertions** | Readable assertions |
| **NSubstitute** | Mocking framework |
| **TestContainers** | Real infrastructure in tests |
| **NetArchTest** | Architecture rule verification |
| **Bogus** | Fake data generation |

## 🏃 Running Tests

```bash
# Run all tests
dotnet test

# Run specific project
dotnet test tests/DotNetAtlas.Domain.UnitTests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific category
dotnet test --filter "Category=Unit"
```

## 📖 Further Reading

- [**TestContainers**](TestContainers.md) - Real infrastructure testing
- [**Architecture Tests**](ArchitectureTests.md) - Enforcing rules
- [**CI/CD**](../devops/CICD.md) - Running tests in pipelines

