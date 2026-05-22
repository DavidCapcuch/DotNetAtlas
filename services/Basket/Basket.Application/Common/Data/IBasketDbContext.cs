using Platform.ReliableMessaging.Outbox.EFCore;

namespace Basket.Application.Common.Data;

/// <summary>
/// Application-layer abstraction over the Basket service's SQL side-car <see cref="IOutboxDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-0003, Basket is a technical bounded context whose primary store is Redis; the only
/// purpose of PostgreSQL in this service is the transactional outbox (and a future inbox
/// for consuming Catalog invalidation events). This interface therefore exposes no
/// aggregate <c>DbSet</c> — an architecture test asserts this constraint. Command handlers
/// and the outbox publisher handler depend on this interface rather than the concrete
/// <c>BasketDbContext</c> to keep the Application layer decoupled from Infrastructure.
/// </para>
/// <para>
/// The concrete implementation lives in <c>Basket.Infrastructure</c>.
/// </para>
/// </remarks>
public interface IBasketDbContext : IOutboxDbContext;
