<div align="center">

# 🔐 Authentication

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses FusionAuth as the identity provider with OpenID Connect (OIDC). The API validates JWT tokens, extracts claims, and provides an `ICurrentUser` abstraction for accessing user information in handlers. |

Authentication verifies who users are. DotNetAtlas integrates with FusionAuth, a modern identity platform, using standard OIDC/OAuth2 protocols.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Client App                              │
│  1. Redirect to FusionAuth login                             │
│  2. Receive authorization code                               │
│  3. Exchange code for tokens                                 │
│  4. Include access token in API requests                     │
└────────────────────────────┬────────────────────────────────┘
                             │ Authorization: Bearer <token>
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    DotNetAtlas API                           │
│  1. Validate JWT signature (using FusionAuth public keys)    │
│  2. Validate claims (issuer, audience, expiration)           │
│  3. Extract user info into ICurrentUser                      │
│  4. Authorize based on roles/permissions                     │
└─────────────────────────────────────────────────────────────┘
                             │
                             │ Fetch JWKS
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                      FusionAuth                              │
│  - User management                                           │
│  - Token issuance                                            │
│  - JWKS endpoint for signature verification                  │
└─────────────────────────────────────────────────────────────┘
```

## 🔧 Configuration

### JWT Bearer Authentication

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "http://localhost:9011";
        options.Audience = "dotnetatlas-api";
        options.RequireHttpsMetadata = false; // Dev only
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "http://localhost:9011",
            ValidateAudience = true,
            ValidAudience = "dotnetatlas-api",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });
```

### Authorization Policies

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("RequireUserRole", policy =>
        policy.RequireRole("user"));
    
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole("admin"));
    
    options.AddPolicy("CanSubmitFeedback", policy =>
        policy.RequireClaim("permissions", "feedback:write"));
});
```

## 👤 ICurrentUser Abstraction

The `ICurrentUser` interface provides access to the authenticated user:

```csharp
public interface ICurrentUser
{
    Guid Id { get; }
    string Email { get; }
    string Name { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? GetClaim(string claimType);
}
```

### Implementation

```csharp
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public Guid Id => Guid.Parse(
        _httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value 
        ?? throw new UnauthorizedAccessException());
    
    public string Email => 
        _httpContextAccessor.HttpContext?.User.FindFirst("email")?.Value 
        ?? string.Empty;
    
    public string Name => 
        _httpContextAccessor.HttpContext?.User.FindFirst("name")?.Value 
        ?? string.Empty;
    
    public IReadOnlyList<string> Roles => 
        _httpContextAccessor.HttpContext?.User
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() 
        ?? [];
    
    public bool IsAuthenticated => 
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
    
    public bool IsInRole(string role) => Roles.Contains(role);
    
    public string? GetClaim(string claimType) => 
        _httpContextAccessor.HttpContext?.User.FindFirst(claimType)?.Value;
}
```

### Registration

```csharp
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, CurrentUser>();
```

## 🔒 Protecting Endpoints

### FastEndpoints

```csharp
public class SendFeedbackEndpoint : Endpoint<SendFeedbackRequest, SendFeedbackResponse>
{
    public override void Configure()
    {
        Post("/api/v1/weather/feedback");
        Roles("user");  // Requires "user" role
    }
}

public class AdminEndpoint : Endpoint<AdminRequest, AdminResponse>
{
    public override void Configure()
    {
        Get("/api/v1/admin/stats");
        Policies("RequireAdminRole");  // Uses named policy
    }
}
```

### Using ICurrentUser in Handlers

```csharp
public class SendFeedbackHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private readonly ICurrentUser _currentUser;
    
    public async Task<Result<Guid>> HandleAsync(SendFeedbackCommand command, CancellationToken ct)
    {
        // Access authenticated user
        var userId = _currentUser.Id;
        var userEmail = _currentUser.Email;
        
        var feedback = Feedback.Create(
            text, 
            rating, 
            userId);  // Associate with user
        
        // ...
    }
}
```

## 🎫 Token Structure

FusionAuth issues JWTs with these claims:

```json
{
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "email": "user@example.com",
  "name": "John Doe",
  "roles": ["user"],
  "permissions": ["feedback:read", "feedback:write"],
  "aud": "dotnetatlas-api",
  "iss": "http://localhost:9011",
  "exp": 1705312800,
  "iat": 1705309200
}
```

## ⚙️ Configuration

```json
{
  "Authentication": {
    "Authority": "http://localhost:9011",
    "Audience": "dotnetatlas-api",
    "RequireHttpsMetadata": false
  }
}
```

## 🐳 FusionAuth in Docker

```yaml
fusionauth:
  image: fusionauth/fusionauth-app:latest
  ports:
    - "9011:9011"
  environment:
    DATABASE_URL: jdbc:postgresql://postgres:5432/fusionauth
    FUSIONAUTH_APP_MEMORY: 512M
```

## 📖 Further Reading

- [**Quick Start**](../getting-started/QuickStart.md) - Getting FusionAuth running
- [FusionAuth Documentation](https://fusionauth.io/docs/)
- [JWT.io](https://jwt.io/) - JWT debugger

