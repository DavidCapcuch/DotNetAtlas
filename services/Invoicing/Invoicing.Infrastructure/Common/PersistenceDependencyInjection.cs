using EntityFramework.Exceptions.PostgreSQL;
using Invoicing.Application.Common.Data;
using Invoicing.Application.Common.Numbering;
using Invoicing.Infrastructure.Common.Config;
using Invoicing.Infrastructure.Persistence.Database;
using Invoicing.Infrastructure.Persistence.Database.Interceptors;
using Invoicing.Infrastructure.Persistence.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.Infrastructure.Common;

/// <summary>
/// DI wiring for the Invoicing persistence slice (M5): Npgsql, EF Core,
/// <see cref="InvoicingDbContext"/>, the gap-free number allocators per
/// ADR-0018, and the <see cref="IInvoicingDbContext"/> port binding.
/// Outbox / inbox / projections land in M6 + M7 alongside the consumers and
/// command handlers that drive them.
/// </summary>
internal static class PersistenceDependencyInjection
{
    internal static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableSensitiveDataLogging,
        bool isDeployedEnvironment)
    {
        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .BindConfiguration(EfCoreOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.Section)
            .ValidateDataAnnotations();

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        // M7 — DispatchDomainEventsInterceptor must run in the same DI scope as the DbContext
        // so that outbox publishers (which inject ITransactionalOutbox<InvoicingDbContext>)
        // resolve to the same scoped UoW the aggregate save commits. See the interceptor's
        // class doc for why this matters for transactional reliability.
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // EnableRetryOnFailure is intentionally NOT applied here. The gap-free
        // allocator pattern (ADR-0018) requires the IssueInvoice / IssueCreditNote
        // handlers to own the transaction via Database.BeginTransactionAsync —
        // a usage shape that NpgsqlRetryingExecutionStrategy refuses with
        // InvalidOperationException. Transient-failure recovery is delegated to
        // the outbox-relay retry loop (the external event re-publishes on retry;
        // a half-issued invoice is impossible because the FOR UPDATE row lock +
        // SaveChangesAsync commit are atomic with the outbox row insert).
        services.AddDbContext<InvoicingDbContext>((sp, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Invoicing)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        InvoicingDbContext.DefaultSchemaName);
                    npgsqlOptions.UseQuerySplittingBehavior(
                        efCoreOptions.UseQuerySplitting
                            ? QuerySplittingBehavior.SplitQuery
                            : QuerySplittingBehavior.SingleQuery);
                })
            .UseSnakeCaseNamingConvention()
            // EF Core sensitive-data logging dumps query parameters (including PII-bearing
            // _enc columns per ADR-0011). Caller (Program.cs) gates this to non-deployed
            // environments only (Development + Test + Testing test-host) so deployed
            // Staging/Production never leak PII into log shippers.
            .EnableSensitiveDataLogging(enableSensitiveDataLogging)
            // CAT-SEC-009: detailed errors leak EF parameter/column info into exception
            // responses. Honour the config flag in non-deployed envs only; force off in
            // deployed environments regardless of config.
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors && !isDeployedEnvironment)
            .UseExceptionProcessor()
            .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IInvoicingDbContext>(sp => sp.GetRequiredService<InvoicingDbContext>());
        services.AddScoped<IInvoiceNumberAllocator, PostgresInvoiceNumberAllocator>();
        services.AddScoped<ICreditNoteNumberAllocator, PostgresCreditNoteNumberAllocator>();

        return services;
    }
}
