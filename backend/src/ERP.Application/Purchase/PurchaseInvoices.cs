using ERP.Application.Abstractions.Messaging;

namespace ERP.Application.Purchase;

/// <summary>Posts a draft purchase: goods in, debt raised, books written.</summary>
/// <param name="PurchaseInvoiceId">The document to post.</param>
/// <param name="CreditDays">
/// How long the firm has to pay, where it differs from the supplier's own terms.
/// </param>
/// <remarks>
/// <para>
/// One command rather than three, and one transaction. A purchase that received its stock
/// but failed to raise its bill would be goods on the shelf that nobody is owed for, and
/// no storekeeper could be expected to notice - so the whole thing lands or none of it
/// does.
/// </para>
/// <para>
/// Posting is deliberately its own step, separate from entering the document. A draft can
/// be corrected; a posted purchase has moved stock and created a debt, and is cancelled
/// rather than edited.
/// </para>
/// </remarks>
public sealed record PostPurchaseInvoiceCommand(Guid PurchaseInvoiceId, int? CreditDays = null)
    : ICommand<PostPurchaseInvoiceResponse>, ITransactional;

/// <summary>What posting a purchase produced.</summary>
/// <param name="PurchaseInvoiceId">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="StockDocumentId">The receipt that put the goods on the shelf.</param>
/// <param name="StockDocumentNumber">The number that receipt's own series issued.</param>
/// <param name="BillId">
/// The bill now owed to the supplier. Absent on a return, which debits a debt rather than
/// creating one.
/// </param>
/// <param name="JournalVoucherId">The journal raised in the nominal ledger.</param>
/// <param name="Total">What the supplier is owed.</param>
public sealed record PostPurchaseInvoiceResponse(
    Guid PurchaseInvoiceId,
    string Number,
    Guid StockDocumentId,
    string StockDocumentNumber,
    Guid? BillId,
    Guid JournalVoucherId,
    decimal Total);
