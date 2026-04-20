namespace Platform.SharedKernel.Pii;

/// <summary>
/// Marks a type, property, or field as carrying Personally Identifiable Information (ADR-0011).
/// </summary>
/// <remarks>
/// <para>
/// The attribute is a policy marker, not a transformation. Consumers in <c>Platform.ServiceDefaults</c>
/// (Serilog destructuring policy, OTel span-attribute processor) and future v2 crypto-shredding
/// tooling look for this attribute to decide how to handle the value.
/// </para>
/// <para>
/// Apply at the narrowest level that describes the data: prefer a property marker over a class marker
/// unless the entire type is PII (e.g., <c>Address</c>).
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class
    | AttributeTargets.Struct
    | AttributeTargets.Property
    | AttributeTargets.Field,
    Inherited = false,
    AllowMultiple = false)]
public sealed class PiiAttribute : Attribute
{
}
