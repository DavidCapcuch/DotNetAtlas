<div align="center">

# 🐳 TestContainers

</div>

| ⚡ TL;DR |
| -------- |
| TestContainers spins up real Docker containers (SQL Server, Redis, Kafka) for integration tests. Tests run against actual infrastructure, not mocks, catching real integration issues. Containers are created per test class and cleaned up automatically. |

TestContainers provides throwaway instances of databases, message brokers, and other services for testing. No more "works on my machine" - tests run against the same infrastructure as production.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Test Execution                            │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Integration Test Class                     ││
│  │  - Uses real HTTP client                                ││
│  │  - Connects to real databases                           ││
│  │  - Publishes to real Kafka                              ││
│  └─────────────────────────────────────────────────────────┘│
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    Docker Containers                         │
│  ┌───────────┐  ┌───────────┐  ┌───────────┐  ┌───────────┐│
│  │ SQL Server│  │   Redis   │  │   Kafka   │  │ FusionAuth││
│  │  :random  │  │  :random  │  │  :random  │  │  :random  ││
│  └───────────┘  └───────────┘  └───────────┘  └───────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Setup

### Package References

```xml
<PackageReference Include="Testcontainers" Version="4.0.0" />
<PackageReference Include="Testcontainers.MsSql" Version="4.0.0" />
<PackageReference Include="Testcontainers.Redis" Version="4.0.0" />
<PackageReference Include="Testcontainers.Kafka" Version="4.0.0" />
```

### Test Fixture

```csharp
public class IntegrationTestFixture : IAsyncLifetime
{
    public MsSqlContainer SqlServer { get; private set; } = null!;
    public RedisContainer Redis { get; private set; } = null!;
    public KafkaContainer Kafka { get; private set; } = null!;
    
    public string SqlConnectionString => SqlServer.GetConnectionString();
    public string RedisConnectionString => Redis.GetConnectionString();
    public string KafkaBootstrapServers => Kafka.GetBootstrapAddress();
    
    public async Task InitializeAsync()
    {
        // Start containers in parallel
        SqlServer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourStrong!Passw0rd")
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
        
        // Run migrations
        await MigrateDatabaseAsync();
    }
    
    private async Task MigrateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<WeatherDbContext>()
            .UseSqlServer(SqlConnectionString)
            .Options;
        
        await using var context = new WeatherDbContext(options);
        await context.Database.MigrateAsync();
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

### Collection Fixture

Share containers across test classes:

```csharp
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
```

## 🧪 Writing Tests

### API Integration Test

```csharp
[Collection("Integration")]
public class WeatherApiTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private HttpClient _client = null!;
    
    public WeatherApiTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace connection strings with TestContainers
                    services.Configure<DatabaseOptions>(options =>
                    {
                        options.ConnectionString = _fixture.SqlConnectionString;
                    });
                    
                    services.Configure<RedisOptions>(options =>
                    {
                        options.ConnectionString = _fixture.RedisConnectionString;
                    });
                    
                    services.Configure<KafkaOptions>(options =>
                    {
                        options.BootstrapServers = _fixture.KafkaBootstrapServers;
                    });
                });
            });
    }
    
    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }
    
    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }
    
    [Fact]
    public async Task GetForecast_ReturnsWeatherData()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/weather/forecast?city=Prague");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadFromJsonAsync<WeatherForecastResponse>();
        content.Should().NotBeNull();
        content!.City.Should().Be("Prague");
    }
    
    [Fact]
    public async Task SendFeedback_StoresInDatabase()
    {
        // Arrange
        var request = new SendFeedbackRequest
        {
            Text = "Great weather service!",
            Rating = 5
        };
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/weather/feedback", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        // Verify in database
        await using var context = CreateDbContext();
        var feedback = await context.Feedback.FirstOrDefaultAsync();
        feedback.Should().NotBeNull();
        feedback!.FeedbackText.Value.Should().Be("Great weather service!");
    }
    
    private WeatherDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WeatherDbContext>()
            .UseSqlServer(_fixture.SqlConnectionString)
            .Options;
        return new WeatherDbContext(options);
    }
}
```

### Database Integration Test

```csharp
[Collection("Integration")]
public class FeedbackRepositoryTests
{
    private readonly IntegrationTestFixture _fixture;
    
    public FeedbackRepositoryTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task Add_PersistsFeedback()
    {
        // Arrange
        await using var context = CreateDbContext();
        var repository = new FeedbackRepository(context);
        
        var feedback = Feedback.Create(
            FeedbackText.Create("Test").Value,
            FeedbackRating.Create(5).Value,
            Guid.CreateVersion7()).Value;
        
        // Act
        repository.Add(feedback);
        await context.SaveChangesAsync();
        
        // Assert
        await using var verifyContext = CreateDbContext();
        var saved = await verifyContext.Feedback.FindAsync(feedback.Id);
        saved.Should().NotBeNull();
    }
}
```

### Kafka Integration Test

```csharp
[Collection("Integration")]
public class KafkaIntegrationTests
{
    private readonly IntegrationTestFixture _fixture;
    
    [Fact]
    public async Task PublishAndConsume_Message()
    {
        // Arrange
        var config = new ProducerConfig
        {
            BootstrapServers = _fixture.KafkaBootstrapServers
        };
        
        using var producer = new ProducerBuilder<string, string>(config).Build();
        
        // Act
        await producer.ProduceAsync("test-topic", new Message<string, string>
        {
            Key = "test-key",
            Value = """{"type":"test","data":"hello"}"""
        });
        
        // Assert - consume and verify
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _fixture.KafkaBootstrapServers,
            GroupId = "test-group",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe("test-topic");
        
        var result = consumer.Consume(TimeSpan.FromSeconds(10));
        result.Message.Value.Should().Contain("hello");
    }
}
```

## ⚡ Performance Tips

| Tip | Benefit |
|-----|---------|
| Use collection fixtures | Share containers across tests |
| Start containers in parallel | Faster startup |
| Reuse containers | Avoid repeated startup cost |
| Use lightweight images | Faster pull and start |

## 🔧 CI/CD Configuration

```yaml
# GitHub Actions
jobs:
  test:
    runs-on: ubuntu-latest
    services:
      docker:
        image: docker:dind
    steps:
      - uses: actions/checkout@v4
      - name: Run integration tests
        run: dotnet test --filter "Category=Integration"
```

## 📖 Further Reading

- [**Testing Overview**](Overview.md) - Testing strategy
- [TestContainers Documentation](https://dotnet.testcontainers.org/)

