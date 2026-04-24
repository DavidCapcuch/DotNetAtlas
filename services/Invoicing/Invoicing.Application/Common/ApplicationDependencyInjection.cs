using Microsoft.Extensions.DependencyInjection;

namespace Invoicing.Application.Common;

/// <summary>
/// DI extensions for the Invoicing application layer.
/// M1 stub — validators, CQRS handlers, domain-event dispatcher, and behaviour chain
/// land in M2/M7. See plan file §§ M2 (domain events) and M7 (command handlers).
/// </summary>
public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddInvoicingApplication(this IServiceCollection services)
    {
        // M2+: AddValidatorsFromAssembly, AddCqrsHandlersFromAssembly,
        //      AddDomainEventHandlersFromAssembly, AddDomainEventDispatcher,
        //      AddOptions<TopicsOptions>, behavior chain (Validation, Metrics, Logging, Tracing).
        return services;
    }
}
