using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Persistence.Database;

namespace Payments.ArchitectureTests.Infrastructure;

/// <summary>
/// ADR-0011 — sensitive payment-instrument tokens persist under <c>*_enc</c> column names so
/// the v2 per-buyer DEK migration can flip plaintext to ciphertext without renaming columns.
/// Today the EF mapping must put <see cref="PaymentTransaction.PaymentMethodId"/> behind
/// <c>payment_method_id_enc</c> and <see cref="PaymentTransaction.GatewayTransactionId"/>
/// behind <c>gateway_transaction_id_enc</c>; mirror the Ordering pattern.
/// </summary>
public sealed class PiiColumnNamingTests
{
    [Theory]
    [InlineData(nameof(PaymentTransaction.PaymentMethodId))]
    [InlineData(nameof(PaymentTransaction.GatewayTransactionId))]
    public void PiiProperty_Column_Should_EndWith_Enc(string propertyName)
    {
        using var context = CreateContextWithoutOpeningConnection();
        var paymentEntity = context.Model.FindEntityType(typeof(PaymentTransaction))
            ?? throw new InvalidOperationException("PaymentTransaction entity not in model");

        var property = paymentEntity.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' missing on PaymentTransaction entity");

        var columnName = GetColumnName(property);

        columnName.Should().NotBeNull();
        columnName!.Should().EndWith(
            "_enc",
            $"PII property '{propertyName}' must persist under a *_enc column per ADR-0011 — " +
            $"actual column name: '{columnName}'");
    }

    private static PaymentsDbContext CreateContextWithoutOpeningConnection()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql("Host=fake;Database=fake;Username=fake;Password=fake")
            .Options;
        return new PaymentsDbContext(options);
    }

    private static string? GetColumnName(IProperty property)
    {
        var storeObject = StoreObjectIdentifier.Create(property.DeclaringType, StoreObjectType.Table);
        return storeObject is { } so ? property.GetColumnName(so) : property.GetColumnName();
    }
}
