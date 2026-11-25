using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>
/// Goods sold to a customer: a sales invoice of kind <see cref="SalesDocumentKind.Invoice"/>,
/// kept in the Sales master, details and additional tables.
/// </summary>
/// <remarks>
/// <para>
/// Everything a sale does it does as a <see cref="SalesInvoice"/>; the type exists so a sale
/// and a return (<see cref="SalesReturn"/>) are each stored in tables of their own, as the
/// business's own ledgers always kept them. A sale is created through
/// <see cref="SalesInvoice.CreateDraft"/>, which picks this type for the kind.
/// </para>
/// <para>
/// Its lines, their tax and serial numbers, and its charges are the sale's own types for
/// the same reason, and are made by the sale itself.
/// </para>
/// </remarks>
public sealed class Sale : SalesInvoice
{
    internal Sale(
        SalesInvoiceId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        LedgerId customerLedgerId,
        WarehouseId warehouseId,
        TaxMode mode,
        CurrencyCode currency,
        SalesInvoiceId? returnsInvoiceId,
        SalesOrderId? salesOrderId)
        : base(
            id,
            tenantId,
            firmId,
            branchId,
            financialYearId,
            SalesDocumentKind.Invoice,
            number,
            date,
            customerLedgerId,
            warehouseId,
            mode,
            currency,
            returnsInvoiceId,
            salesOrderId)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Sale()
    {
    }
}

/// <summary>A line of a sale: the goods going out.</summary>
public sealed class SaleLine : SalesInvoiceLine
{
    internal SaleLine(
        SalesInvoiceLineId id,
        TenantId tenantId,
        SalesInvoiceId saleId,
        ProductId productId,
        BatchId? batchId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        decimal discount,
        TaxAssessment assessment,
        int lineNumber)
        : base(id, tenantId, saleId, productId, batchId, unitId, quantity, stockQuantity, rate, discount, assessment, lineNumber, returned: false)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SaleLine()
    {
    }

    /// <inheritdoc />
    private protected override SalesInvoiceLineSerial NewSerial(SerialNumberId serialId) =>
        new SaleLineSerial(TenantId, Id, serialId);
}

/// <summary>A serial number on a line of a sale.</summary>
public sealed class SaleLineSerial : SalesInvoiceLineSerial
{
    internal SaleLineSerial(TenantId tenantId, SalesInvoiceLineId lineId, SerialNumberId serialNumberId)
        : base(tenantId, lineId, serialNumberId)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SaleLineSerial()
    {
    }
}

/// <summary>A tax component on a line of a sale.</summary>
public sealed class SaleLineTax : SalesInvoiceLineTax
{
    internal SaleLineTax(
        TenantId tenantId,
        SalesInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
        : base(tenantId, lineId, type, percentage, amount)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SaleLineTax()
    {
    }
}

/// <summary>A charge on a sale: its additional table.</summary>
public sealed class SaleCharge : SalesInvoiceCharge
{
    internal SaleCharge(
        SalesInvoiceChargeId id,
        TenantId tenantId,
        SalesInvoiceId saleId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id, tenantId, saleId, ledgerId, amount, isAddition)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SaleCharge()
    {
    }
}
