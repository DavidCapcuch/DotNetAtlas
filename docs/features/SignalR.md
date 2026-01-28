<div align="center">

# 📡 Real-time with SignalR

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses SignalR for real-time communication with a Redis backplane for horizontal scaling. Weather alerts are pushed to subscribed clients. The `/signalr-ui` page provides a test interface. |

SignalR enables real-time, bidirectional communication between server and clients. DotNetAtlas uses it to push weather alerts to subscribed users.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Browser Clients                           │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐                      │
│  │Client 1 │  │Client 2 │  │Client 3 │                      │
│  └────┬────┘  └────┬────┘  └────┬────┘                      │
└───────┼────────────┼────────────┼───────────────────────────┘
        │            │            │
        │ WebSocket  │ WebSocket  │ WebSocket
        ▼            ▼            ▼
┌─────────────────────────────────────────────────────────────┐
│                  API Instance 1                              │
│              SignalR Hub + Redis Backplane                   │
└────────────────────────────┬────────────────────────────────┘
                             │
                             │ Pub/Sub
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                        Redis                                 │
│              Backplane for message distribution              │
└────────────────────────────┬────────────────────────────────┘
                             │
                             │ Pub/Sub
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  API Instance 2                              │
│              SignalR Hub + Redis Backplane                   │
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Configuration

### Server Setup

```csharp
// Add SignalR with Redis backplane
services.AddSignalR()
    .AddStackExchangeRedis("localhost:6379", options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("DotNetAtlas");
    });

// Map hub endpoint
app.MapHub<WeatherAlertsHub>("/hubs/weather-alerts");
```

### Hub Implementation

```csharp
public class WeatherAlertsHub : Hub
{
    private readonly ILogger<WeatherAlertsHub> _logger;
    
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "Client connected: {ConnectionId}", 
            Context.ConnectionId);
        
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}", 
            Context.ConnectionId);
        
        await base.OnDisconnectedAsync(exception);
    }
    
    /// <summary>
    /// Subscribe to alerts for a specific city.
    /// </summary>
    public async Task SubscribeToCity(string city)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"city:{city}");
        
        _logger.LogInformation(
            "Client {ConnectionId} subscribed to {City}",
            Context.ConnectionId, city);
    }
    
    /// <summary>
    /// Unsubscribe from alerts for a specific city.
    /// </summary>
    public async Task UnsubscribeFromCity(string city)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"city:{city}");
    }
}
```

### Sending Messages

From anywhere in the application:

```csharp
public class WeatherAlertService
{
    private readonly IHubContext<WeatherAlertsHub> _hubContext;
    
    public async Task SendAlertAsync(string city, WeatherAlert alert)
    {
        // Send to all clients subscribed to this city
        await _hubContext.Clients
            .Group($"city:{city}")
            .SendAsync("ReceiveAlert", alert);
    }
    
    public async Task BroadcastAsync(WeatherAlert alert)
    {
        // Send to all connected clients
        await _hubContext.Clients.All
            .SendAsync("ReceiveAlert", alert);
    }
}
```

### From Kafka Consumer

```csharp
public class WeatherAlertEventHandler : IMessageHandler<WeatherAlertEvent>
{
    private readonly IHubContext<WeatherAlertsHub> _hubContext;
    
    public async Task Handle(IMessageContext context, WeatherAlertEvent message)
    {
        var alert = new WeatherAlert
        {
            City = message.City,
            AlertType = message.AlertType,
            Message = message.Message,
            Severity = message.Severity
        };
        
        await _hubContext.Clients
            .Group($"city:{message.City}")
            .SendAsync("ReceiveAlert", alert);
    }
}
```

## 🖥️ Client Integration

### JavaScript Client

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/weather-alerts")
    .withAutomaticReconnect()
    .build();

// Handle incoming alerts
connection.on("ReceiveAlert", (alert) => {
    console.log(`Alert for ${alert.city}: ${alert.message}`);
    showNotification(alert);
});

// Start connection
await connection.start();

// Subscribe to a city
await connection.invoke("SubscribeToCity", "Prague");
```

### .NET Client

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5000/hubs/weather-alerts")
    .WithAutomaticReconnect()
    .Build();

connection.On<WeatherAlert>("ReceiveAlert", alert =>
{
    Console.WriteLine($"Alert: {alert.Message}");
});

await connection.StartAsync();
await connection.InvokeAsync("SubscribeToCity", "Prague");
```

## 🔐 Authentication

SignalR uses the same JWT authentication as the API:

```csharp
services.AddSignalR();

// Hub requires authentication
[Authorize]
public class WeatherAlertsHub : Hub
{
    public string UserId => Context.User?.FindFirst("sub")?.Value ?? "";
}
```

Client sends token:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/weather-alerts", {
        accessTokenFactory: () => getAccessToken()
    })
    .build();
```

## 🧪 Test UI

DotNetAtlas includes a test page at `/signalr-ui`:

```html
<!-- Pages/SignalRTest.cshtml -->
<div id="alerts"></div>
<input id="city" placeholder="City" />
<button onclick="subscribe()">Subscribe</button>

<script>
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/weather-alerts")
        .build();
    
    connection.on("ReceiveAlert", (alert) => {
        document.getElementById("alerts").innerHTML += 
            `<div>${alert.city}: ${alert.message}</div>`;
    });
    
    connection.start();
    
    function subscribe() {
        const city = document.getElementById("city").value;
        connection.invoke("SubscribeToCity", city);
    }
</script>
```

## 🔭 Observability

SignalR operations are traced:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Microsoft.AspNetCore.SignalR"));
```

## ⚙️ Configuration

```json
{
  "SignalR": {
    "Redis": {
      "ConnectionString": "localhost:6379",
      "ChannelPrefix": "DotNetAtlas"
    },
    "KeepAliveInterval": "00:00:15",
    "ClientTimeoutInterval": "00:00:30"
  }
}
```

## 📖 Further Reading

- [**Quick Start**](../getting-started/QuickStart.md) - Running the test UI
- [SignalR Documentation](https://docs.microsoft.com/aspnet/core/signalr/)

