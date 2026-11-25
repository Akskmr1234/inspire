using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// Which transaction a voucher's entries were generated from, kept on the voucher so the
/// common voucher table leads back to the sale, purchase, return or stock document behind
/// every debit and credit.
/// </summary>
/// <remarks>
/// The documents already name their vouchers (an invoice its journal and its receipt, a
/// stock document its journal, a cheque its clearing and its reversal). The link the other
/// way is written from those as they are saved (<c>VoucherSourceInterceptor</c>), and was
/// filled in from them for vouchers that existed before.
/// </remarks>
public static class VoucherSources
{
    /// <summary>The kind of transaction: Sales, SalesReturn, Purchase, StockAdjustment...</summary>
    public const string Type = "SourceType";

    /// <summary>The transaction's id.</summary>
    public const string Id = "SourceId";

    /// <summary>The transaction's number: its reference number.</summary>
    public const string Number = "SourceNumber";

    /// <summary>The longest a kind may be.</summary>
    public const int MaximumTypeLength = 40;

    /// <summary>The longest a number may be.</summary>
    public const int MaximumNumberLength = 60;

    /// <summary>Maps the link to its columns.</summary>
    /// <param name="builder">The voucher's entity builder.</param>
    public static void Configure(EntityTypeBuilder<Voucher> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<string?>(Type).HasColumnName("source_type").HasMaxLength(MaximumTypeLength);
        builder.Property<Guid?>(Id).HasColumnName("source_id");
        builder.Property<string?>(Number).HasColumnName("source_number").HasMaxLength(MaximumNumberLength);
        builder.HasIndex(Id).HasDatabaseName("ix_vouchers_source");
    }

    /// <summary>The vouchers a document names, with what each should say it came from.</summary>
    /// <param name="entity">A tracked entity.</param>
    /// <returns>The links; none for anything that names no voucher.</returns>
    public static IEnumerable<VoucherSource> For(object entity)
    {
        switch (entity)
        {
            case SalesInvoice invoice:
                string sale = invoice.IsReturn ? "SalesReturn" : "Sales";
                yield return new(invoice.JournalVoucherId, 1, sale, invoice.Id.Value, invoice.Number);
                yield return new(invoice.ReceiptVoucherId, 1, sale, invoice.Id.Value, invoice.Number);
                break;

            case PurchaseInvoice invoice:
                yield return new(
                    invoice.JournalVoucherId,
                    1,
                    invoice.IsReturn ? "PurchaseReturn" : "Purchase",
                    invoice.Id.Value,
                    invoice.Number);
                break;

            case StockDocument document:
                yield return new(document.JournalVoucherId, 2, document.Type.ToString(), document.Id.Value, document.Number);
                break;

            case Cheque cheque:
                yield return new(cheque.ClearingVoucherId, 3, "ChequeClearing", cheque.Id.Value, cheque.ChequeNumber);
                yield return new(cheque.ReversalVoucherId, 3, "ChequeReversal", cheque.Id.Value, cheque.ChequeNumber);
                break;

            default:
                break;
        }
    }
}

/// <summary>A voucher, and the transaction it should say it came from.</summary>
/// <param name="VoucherId">The voucher, if the document has one yet.</param>
/// <param name="Priority">Which claim wins where two documents name the same voucher: the lower.</param>
/// <param name="Type">The kind of transaction.</param>
/// <param name="Id">The transaction.</param>
/// <param name="Number">Its number.</param>
public sealed record VoucherSource(VoucherId? VoucherId, int Priority, string Type, Guid Id, string Number);
