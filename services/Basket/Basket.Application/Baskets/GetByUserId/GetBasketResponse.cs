using Basket.Application.Baskets.Common.Contracts;

namespace Basket.Application.Baskets.GetByUserId;

/// <summary>
/// Response DTO for <see cref="GetBasketByUserIdQuery"/>. Absent-basket callers
/// receive an instance with <see cref="Version"/> <c>= 0</c>, an empty
/// <see cref="Items"/> collection, <see cref="Total"/> <c>= null</c>, and default
/// timestamps — semantically "your basket is empty" rather than "not found".
/// </summary>
public sealed record GetBasketResponse
{
    public required Guid UserId { get; init; }

    public required int Version { get; init; }

    public required IReadOnlyList<GetBasketItemDto> Items { get; init; }

    public required MoneyDto? Total { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset LastModifiedAtUtc { get; init; }
}

/// <summary>
/// One basket line in the <see cref="GetBasketResponse"/>.
/// </summary>
public sealed record GetBasketItemDto
{
    public required Guid ProductId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required MoneyDto SnapshotPrice { get; init; }

    public required int Quantity { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required MoneyDto LineTotal { get; init; }
}
