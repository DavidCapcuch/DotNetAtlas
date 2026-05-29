namespace Payments.Api.Common;

/// <summary>
/// Wires the presentation layer for Payments: FastEndpoints + Swagger and ProblemDetails.
/// Authentication lives in <see cref="AuthenticationDependencyInjection"/> and is wired
/// explicitly from Program.cs. Payments has no state-changing HTTP endpoints in v1, so
/// ADR-0013's idempotency-key output cache is intentionally NOT wired here. Payments is
/// an admin/internal API — no CORS is wired.
/// </summary>
internal static class ApiDependencyInjection
{
    internal static IServiceCollection AddPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPaymentsFastEndpoints(configuration);

        services.AddProblemDetails();

        services.AddRazorPages();

        return services;
    }
}
