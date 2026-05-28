using Invoicing.Application.Common.Exceptions;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.UnitTests.Common.Exceptions;

public class InvoiceTotalMismatchExceptionTests
{
    [Fact]
    public void Constructor_PreservesTypedFields()
    {
        var correlationId = Guid.CreateVersion7();

        var ex = new InvoiceTotalMismatchException(
            orderTotal: 152.00m,
            paymentAmount: 150.00m,
            correlationId: correlationId);

        ex.OrderTotal.Should().Be(152.00m);
        ex.PaymentAmount.Should().Be(150.00m);
        ex.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public void Constructor_SetsCanonicalErrorCode()
    {
        var ex = new InvoiceTotalMismatchException(
            orderTotal: 1m,
            paymentAmount: 2m,
            correlationId: Guid.CreateVersion7());

        ex.ErrorCode.Should().Be("Invoicing.TotalMismatch");
    }

    [Fact]
    public void Constructor_EmbedsFieldsInMessage()
    {
        var correlationId = Guid.CreateVersion7();
        var orderTotal = 99.99m;
        var paymentAmount = 100.00m;

        var ex = new InvoiceTotalMismatchException(orderTotal, paymentAmount, correlationId);

        // The composed message uses string interpolation, which formats decimals via the
        // current culture — assert with the same culture so the test runs on any locale
        // (CI: en-US dot, dev: cs-CZ comma).
        ex.Message.Should().Contain(orderTotal.ToString());
        ex.Message.Should().Contain(paymentAmount.ToString());
        ex.Message.Should().Contain(correlationId.ToString());
    }

    [Fact]
    public void InheritsDataIntegrityException_SoExistingDltBranchCatchesIt()
    {
        var ex = new InvoiceTotalMismatchException(
            orderTotal: 1m,
            paymentAmount: 2m,
            correlationId: Guid.CreateVersion7());

        // The consumer middleware DLTs on `catch (CriticalException)`; this assertion
        // locks the inheritance chain so a future refactor doesn't silently break that
        // routing (CriticalException → DataIntegrityException → InvoiceTotalMismatchException).
        ex.Should().BeAssignableTo<DataIntegrityException>();
        ex.Should().BeAssignableTo<CriticalException>();
    }
}
