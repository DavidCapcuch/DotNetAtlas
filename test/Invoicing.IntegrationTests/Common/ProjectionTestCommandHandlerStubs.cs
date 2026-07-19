using FluentResults;
using Invoicing.Application.CreditNotes.IssueCreditNote;
using Invoicing.Application.Invoices.IssueInvoice;
using NSubstitute;
using Platform.CQRS;

namespace Invoicing.IntegrationTests.Common;

/// <summary>
/// No-op stubs for the command handlers, used by the projection tests so they can
/// keep asserting the projection-layer behaviour in isolation. The real handlers run
/// inside the inbox transaction in production (see <c>OrderConfirmedInvoiceProjectionKafkaHandler</c>),
/// but the tests inject these stubs to keep their assertions on
/// <c>pending_invoices.IssuedInvoiceId IS NULL</c> / <c>pending_credit_notes.IssuedCreditNoteId IS NULL</c>
/// stable. The BC's own integration tests exercise the full flow with the real handler.
/// </summary>
internal static class ProjectionTestCommandHandlerStubs
{
    public static ICommandHandler<IssueInvoiceCommand, Guid> NoOpIssueInvoiceHandler()
    {
        var stub = Substitute.For<ICommandHandler<IssueInvoiceCommand, Guid>>();
        stub.HandleAsync(Arg.Any<IssueInvoiceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(Guid.Empty)));
        return stub;
    }

    public static ICommandHandler<IssueCreditNoteCommand, Guid> NoOpIssueCreditNoteHandler()
    {
        var stub = Substitute.For<ICommandHandler<IssueCreditNoteCommand, Guid>>();
        stub.HandleAsync(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(Guid.Empty)));
        return stub;
    }
}
