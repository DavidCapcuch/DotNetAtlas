using FluentValidation;
using Invoicing.Application.Common.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Invoicing.Application.Common;

/// <summary>
/// Composition root for the Invoicing Application layer. The host project
/// (<c>Invoicing.API</c>) calls <c>services.AddInvoicingApplication()</c>; the Infrastructure
/// layer's <c>AddInvoicingInfrastructure</c> wires the concretions on top.
/// </summary>
/// <remarks>
/// Wired in M7. Registers FluentValidation validators, CQRS command/query handlers,
/// domain-event handlers + dispatcher (the <c>DispatchDomainEventsInterceptor</c> in
/// Infrastructure picks up the dispatcher from this composition root), and the
/// <see cref="InvoicingTopicsOptions"/> binding. The Tracing → Logging → Metrics →
/// Validation behaviour chain lands in M8 once query + parameterless-command handlers
/// exist (Platform.CQRS's Decorate calls require all three handler shapes to have at
/// least one registration each). <c>BlobStorageOptions</c> is registered in Infrastructure
/// (it injects the connection string) and consumed by the M7 command handlers via DI.
/// </remarks>
public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddInvoicingApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = typeof(ApplicationDependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddCqrsHandlersFromAssembly(assembly);
        services
            .AddDomainEventHandlersFromAssembly(assembly)
            .AddDomainEventDispatcher();

        // The CQRS behaviour chain (Tracing → Logging → Metrics → Validation) is intentionally
        // NOT registered in M7. Platform.CQRS's helpers decorate three handler shapes —
        // ICommandHandler<T>, ICommandHandler<T, R>, IQueryHandler<T, R> — and Scrutor's
        // Decorate throws if any shape has no registered implementation. M7 only has
        // ICommandHandler<T, R> handlers (IssueInvoice / IssueCreditNote both return Guid).
        // M8 lands the HTTP query handlers (GetInvoiceById, GetInvoicesByBuyer) and the
        // ResendInvoiceCommand (parameterless ICommandHandler<T>); at that point all three
        // shapes are present and the behaviour chain can be enabled. For M7 the OTel /
        // logging gap is filled by the M6 KafkaFlow consumer middleware (correlation-id +
        // tracing on the inbound message) and the handler's own ILogger.

        services.AddOptionsWithValidateOnStart<InvoicingTopicsOptions>()
            .BindConfiguration(InvoicingTopicsOptions.Section)
            .ValidateDataAnnotations();

        // BlobStorageOptions registration lives in Infrastructure (it injects the
        // ConnectionStrings:AzureStorage value into the same options object); the
        // M7 command handlers consume it via IOptions<BlobStorageOptions> from DI.

        return services;
    }
}
