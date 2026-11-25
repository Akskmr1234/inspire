using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence;

/// <summary>
/// A transaction's totals kept on its master row, and each line's total on its detail row,
/// for reports and for anything else that reads the tables directly.
/// </summary>
/// <remarks>
/// <para>
/// The figures are never typed in: the domain works them out from the lines and the
/// charges, exactly as it always has, and they are copied to the master on every save
/// that has the lines and charges in hand (<c>StoredTotalsInterceptor</c>). A line's
/// total is the database's own sum of its taxable amount and its tax, so it cannot
/// disagree with them.
/// </para>
/// <para>
/// Documents saved before the columns existed start with them empty and are filled in
/// the background after the API starts (<c>StoredTotalsBackfill</c>).
/// </para>
/// </remarks>
public static class StoredTotals
{
    /// <summary>The master's goods before tax.</summary>
    public const string Taxable = "StoredTaxable";

    /// <summary>The master's tax on the goods.</summary>
    public const string Tax = "StoredTax";

    /// <summary>What the charges add, net of what they deduct.</summary>
    public const string Charges = "StoredCharges";

    /// <summary>The total before rounding.</summary>
    public const string Gross = "StoredGross";

    /// <summary>The rounding difference.</summary>
    public const string Rounding = "StoredRounding";

    /// <summary>The transaction total.</summary>
    public const string Total = "StoredTotal";

    /// <summary>A line's taxable amount plus its tax.</summary>
    public const string LineTotal = "StoredLineTotal";

    /// <summary>The master's totals, in the order <see cref="For"/> gives them.</summary>
    public static IReadOnlyList<string> MasterProperties { get; } = [Taxable, Tax, Charges, Gross, Rounding, Total];

    /// <summary>The types whose masters carry totals.</summary>
    public static IReadOnlyList<Type> MasterTypes { get; } =
        [typeof(SalesInvoice), typeof(PurchaseInvoice), typeof(SalesOrder), typeof(PurchaseOrder)];

    /// <summary>Maps a master's totals to their columns.</summary>
    /// <typeparam name="T">The master.</typeparam>
    /// <param name="builder">The entity builder.</param>
    public static void ConfigureMaster<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        Column(builder, Taxable, "taxable_total");
        Column(builder, Tax, "tax_total");
        Column(builder, Charges, "charge_total");
        Column(builder, Gross, "gross_total");
        Column(builder, Rounding, "rounding_difference");
        Column(builder, Total, "total");
    }

    /// <summary>Maps a line's total: the database's sum of its taxable amount and tax.</summary>
    /// <typeparam name="T">The line.</typeparam>
    /// <param name="builder">The entity builder.</param>
    public static void ConfigureLine<T>(EntityTypeBuilder<T> builder)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property<decimal>(LineTotal)
            .HasColumnName("line_total")
            .HasPrecision(19, 4)
            .HasComputedColumnSql("taxable_amount + tax_amount", stored: true);
    }

    /// <summary>A master's totals, worked out by the domain.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The totals, in the order of <see cref="MasterProperties"/>; null for any other type.</returns>
    public static decimal[]? For(object entity) => entity switch
    {
        SalesInvoice invoice => Amounts(
            invoice.Taxable, invoice.Tax, invoice.ChargeTotal, invoice.GrossTotal, invoice.RoundingDifference, invoice.Total),
        PurchaseInvoice invoice => Amounts(
            invoice.Taxable, invoice.Tax, invoice.ChargeTotal, invoice.GrossTotal, invoice.RoundingDifference, invoice.Total),
        SalesOrder order => Amounts(
            order.Taxable, order.Tax, order.ChargeTotal, order.GrossTotal, order.RoundingDifference, order.Total),
        PurchaseOrder order => Amounts(
            order.Taxable, order.Tax, order.ChargeTotal, order.GrossTotal, order.RoundingDifference, order.Total),
        _ => null,
    };

    private static decimal[] Amounts(params Money[] amounts) => [.. amounts.Select(amount => amount.Amount)];

    private static void Column<T>(EntityTypeBuilder<T> builder, string property, string column)
        where T : class =>
        builder.Property<decimal?>(property).HasColumnName(column).HasPrecision(19, 4);
}
