namespace Platform.Api.UnitTests.Swagger.Probe;

/// <summary>
/// Response contract spanning the nullability × C#-<c>required</c> matrix the OpenAPI document must
/// resolve. <see cref="Price"/> (non-nullable, not <c>required</c>) and <see cref="Note"/>
/// (<c>required</c>, nullable) disagree about which modifier decides membership of the document's
/// <c>required</c> array, so a document driven by the wrong one fails.
/// </summary>
public sealed record ContractProbeResponse
{
    public required Guid ProductId { get; init; }

    /// <summary>
    /// Non-nullable reference type. NJsonSchema decides this from the NRT annotation rather than
    /// from a value type's inherent non-nullability — a distinct path, and the commonest shape in
    /// this repo's response contracts.
    /// </summary>
    public required string Sku { get; init; }

    /// <summary>A nested schema of its own, so the rule is proven past the top level.</summary>
    public ProbeMoney Price { get; init; } = new();

    public required string? Note { get; init; }
}

/// <summary>
/// Nested value shape reached through <see cref="ContractProbeResponse.Price"/>. NSwag emits it as
/// its own <c>components.schemas</c> entry behind a <c>$ref</c>, which is the node oasdiff reads.
/// </summary>
public sealed record ProbeMoney
{
    public decimal Amount { get; init; }

    public string? Currency { get; init; }
}
