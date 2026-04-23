using Platform.CQRS;

namespace Basket.Application.Baskets.GetByUserId;

/// <summary>
/// Returns the caller's basket (or an empty one if absent — never 404, per
/// <c>use-cases.md § 2.2.1</c>).
/// </summary>
public sealed record GetBasketByUserIdQuery(Guid UserId) : IQuery<GetBasketResponse>;
