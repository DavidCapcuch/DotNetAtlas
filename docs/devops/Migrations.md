<div align="center">

# 🗃️ Database Migrations

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses EF Core migrations for schema changes. Migrations are generated from code, stored in version control, and applied automatically on startup (dev) or via CI/CD (prod). Each bounded context has its own DbContext and migration history. |

Database migrations track schema changes over time. DotNetAtlas uses Entity Framework Core migrations with a code-first approach.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Migration Flow                            │
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Development                           ││
│  │  1. Modify entity/configuration                         ││
│  │  2. Run: dotnet ef migrations add <Name>                ││
│  │  3. Review generated migration                          ││
│  │  4. Commit to version control                           ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   CI/CD Pipeline                        ││
│  │  1. Build application                                   ││
│  │  2. Generate migration script                           ││
│  │  3. Review script (optional gate)                       ││
│  │  4. Apply to target database                            ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## 📦 DbContext Organization

Each bounded context has its own DbContext:

```
src/DotNetAtlas.Infrastructure/
├── Persistence/
│   ├── Weather/
│   │   ├── WeatherDbContext.cs
│   │   ├── Configurations/
│   │   │   ├── FeedbackConfiguration.cs
│   │   │   └── WeatherAlertConfiguration.cs
│   │   └── Migrations/
│   │       ├── 20240115_InitialCreate.cs
│   │       └── 20240120_AddFeedbackRating.cs
│   └── Outbox/
│       ├── OutboxDbContext.cs
│       └── Migrations/
│           └── 20240115_InitialCreate.cs
```

## 🔧 Creating Migrations

### Add a Migration

```bash
# From solution root
dotnet ef migrations add AddFeedbackRating \
    --project src/DotNetAtlas.Infrastructure \
    --startup-project src/DotNetAtlas.Api \
    --context WeatherDbContext \
    --output-dir Persistence/Weather/Migrations
```

### Generated Migration

```csharp
public partial class AddFeedbackRating : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Rating",
            table: "Feedback",
            type: "int",
            nullable: false,
            defaultValue: 0);
        
        migrationBuilder.CreateIndex(
            name: "IX_Feedback_Rating",
            table: "Feedback",
            column: "Rating");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Feedback_Rating",
            table: "Feedback");
        
        migrationBuilder.DropColumn(
            name: "Rating",
            table: "Feedback");
    }
}
```

## 🚀 Applying Migrations

### Development (Automatic)

```csharp
// Program.cs - Apply on startup
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

### Production (Script)

```bash
# Generate idempotent script
dotnet ef migrations script \
    --project src/DotNetAtlas.Infrastructure \
    --startup-project src/DotNetAtlas.Api \
    --context WeatherDbContext \
    --idempotent \
    --output migrations.sql
```

### CI/CD Pipeline

```yaml
- name: Generate migration script
  run: |
    dotnet ef migrations script \
      --project src/DotNetAtlas.Infrastructure \
      --startup-project src/DotNetAtlas.Api \
      --context WeatherDbContext \
      --idempotent \
      --output ${{ github.workspace }}/migrations.sql

- name: Apply migrations
  run: |
    sqlcmd -S ${{ secrets.DB_SERVER }} \
           -d ${{ secrets.DB_NAME }} \
           -U ${{ secrets.DB_USER }} \
           -P ${{ secrets.DB_PASSWORD }} \
           -i migrations.sql
```

## 📋 Best Practices

| Practice | Reason |
|----------|--------|
| Always review generated migrations | EF may generate unexpected changes |
| Use idempotent scripts in production | Safe to run multiple times |
| Never modify applied migrations | Create new migration instead |
| Test migrations on copy of prod data | Catch data issues early |
| Include Down() method | Enable rollback if needed |
| Use transactions | Atomic schema changes |

## 🔄 Common Operations

### Remove Last Migration (Not Applied)

```bash
dotnet ef migrations remove \
    --project src/DotNetAtlas.Infrastructure \
    --startup-project src/DotNetAtlas.Api \
    --context WeatherDbContext
```

### List Migrations

```bash
dotnet ef migrations list \
    --project src/DotNetAtlas.Infrastructure \
    --startup-project src/DotNetAtlas.Api \
    --context WeatherDbContext
```

### Revert to Specific Migration

```bash
dotnet ef database update PreviousMigrationName \
    --project src/DotNetAtlas.Infrastructure \
    --startup-project src/DotNetAtlas.Api \
    --context WeatherDbContext
```

## 🗄️ Migration History Table

EF Core tracks applied migrations in `__EFMigrationsHistory`:

```sql
SELECT * FROM __EFMigrationsHistory;

-- MigrationId                          ProductVersion
-- 20240115000000_InitialCreate         9.0.0
-- 20240120000000_AddFeedbackRating     9.0.0
```

## 📖 Further Reading

- [**Docker**](Docker.md) - Running migrations in containers
- [**CI/CD**](CICD.md) - Automated migration deployment
- [EF Core Migrations](https://docs.microsoft.com/ef/core/managing-schemas/migrations/)

