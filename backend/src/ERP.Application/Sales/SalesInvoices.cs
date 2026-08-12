using ERP.Application.Abstractions.Messaging;

namespace ERP.Application.Sales;

/// <summary>Posts a draft sales invoice: the goods leave, the debt is raised, the books move.</summary>
/// <param name="SalesInvoiceId">The draft to post.</param>
/// <param name="CreditDays">
/// How long the customer has to pay. Defaults to the terms on their own ledger.
/// </param>
/// <remarks>
/// <para>
/// One command rather than three, and one transaction. A sale that issued its stock but
/// failed to raise its bill would be goods gone from the shelf that nobody is owed for,
/// and no operator could be expected to notice - so the whole thing lands or none of it
/// does.
/// </para>
/// <para>
/// Posting is deliberately its own step, separate from entering the invoice. A draft can
/// be corrected; a posted invoice has moved stock and raised a debt, and is cancelled
/// rather than edited.
/// </para>
/// </remarks>
public sealed record PostSalesInvoiceCommand(Guid SalesInvoiceId, int? CreditDays = null)
    : ICommand<PostSalesInvoiceResponse>, ITransactional;

/// <summary>What posting an invoice produced.</summary>
/// <param name="SalesInvoiceId">The invoice.</param>
/// <param name="Number">Its number.</param>
/// <param name="StockDocumentId">The stock document the goods moved on.</param>
/// <param name="StockDocumentNumber">That document's own number.</param>
/// <param name="BillId">
/// The bill the customer now owes. Absent on a return, which credits a debt rather than
/// creating one.
/// </param>
/// <param name="JournalVoucherId">The journal raised in the nominal ledger.</param>
/// <param name="Total">What the customer owes.</param>
/// <remarks>
/// All four identifiers, because a sale genuinely produces four things and the caller
/// should not have to go looking for any of them. It is also what makes the two-document
/// arrangement visible rather than surprising: somebody reading the response sees the
/// issue that the stock ledger will show.
/// </remarks>
public sealed record PostSalesInvoiceResponse(
    Guid SalesInvoiceId,
    string Number,
    Guid StockDocumentId,
    string StockDocumentNumber,
    Guid? BillId,
    Guid JournalVoucherId,
    decimal Total);
