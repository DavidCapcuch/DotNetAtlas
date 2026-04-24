// Payments.ArchitectureTests — scaffold placeholder (Wave 1 / M1). Rules land in M7:
// (a) no DateTime.UtcNow in Payments.Domain (ADR-0015),
// (b) no PAN/CVV-style field names in Domain/Infrastructure (ADR-0011),
// (c) aggregate root has private ctor + static factory,
// (d) domain events end with DomainEvent suffix,
// (e) no concrete gateway SDK imports in Payments.Application.
namespace Payments.ArchitectureTests;

internal static class Placeholder;
