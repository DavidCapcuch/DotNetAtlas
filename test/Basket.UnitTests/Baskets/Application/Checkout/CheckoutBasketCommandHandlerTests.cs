using Basket.Application.Abstractions;
using Basket.Application.Baskets.Checkout;
using Basket.Application.Common.Data;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Errors;
using BasketAggregate = Basket.Domain.Baskets.Basket;

namespace Basket.UnitTests.Baskets.Application.Checkout;

public class CheckoutBasketCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 04, 23, 12, 00, 00, TimeSpan.Zero);

    private readonly IBasketRepository _repo = Substitute.For<IBasketRepository>();
    private readonly ITransactionalOutbox<IBasketDbContext> _outbox =
        Substitute.For<ITransactionalOutbox<IBasketDbContext>>();
    private readonly IDomainEventDispatcher _dispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly FakeTimeProvider _time = new(Now);

    private CheckoutBasketCommandHandler CreateSut() => new(
        _repo,
        _outbox,
        _dispatcher,
        _time,
        NullLogger<CheckoutBasketCommandHandler>.Instance);

    private static CheckoutBasketCommand ValidCommand(Guid userId) => new(
        userId,
        Guid.CreateVersion7(),
        ApplicationTestData.AddressDto(),
        ApplicationTestData.AddressDto("CZ"),
        Guid.CreateVersion7());

    [Fact]
    public async Task Handle_WhenBasketExistsWithItems_DispatchesSavesOutboxAndDeletesRedis()
    {
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 2, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var cmd = ValidCommand(userId);
        var result = await CreateSut().HandleAsync(cmd, TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().Be(cmd.CorrelationId);
            await _dispatcher.Received(1).DispatchAsync(
                Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
            await _outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
            await _repo.Received(1).DeleteAsync(userId, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenBasketMissing_FailsEmptyBasket()
    {
        var userId = Guid.CreateVersion7();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(null));

        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<ValidationError>()
                .Which.ErrorCode.Should().Be("Basket.Empty");
            await _outbox.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_WhenBasketEmpty_FailsEmptyBasket()
    {
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        _ = basket.PopDomainEvents();
        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));

        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors[0].Should().BeOfType<ValidationError>()
                .Which.ErrorCode.Should().Be("Basket.Empty");
        }
    }

    [Fact]
    public async Task Handle_WhenRedisDeleteFails_StillReturnsSuccess()
    {
        var userId = Guid.CreateVersion7();
        var basket = BasketAggregate.Create(userId, Now);
        basket.AddItem(Guid.CreateVersion7(), BasketTestData.Snapshot(), 1, Now);
        _ = basket.PopDomainEvents();

        _repo.GetByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<BasketAggregate?>(basket));
        _outbox.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _repo.DeleteAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail("redis transient"));

        var result = await CreateSut().HandleAsync(
            ValidCommand(userId),
            TestContext.Current.CancellationToken);

        // Outbox is source of truth; Redis delete failure does NOT fail the command.
        result.Should().BeSuccess();
    }
}
