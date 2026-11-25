using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>
/// Goods a customer brings back: a sales invoice that runs the other way, kept in tables of
/// its own - the Sales Return master, details and additional charges.
/// </summary>
/// <remarks>
/// <para>
/// Everything a return does it does as a <see cref="SalesInvoice"/> of kind
/// <see cref="SalesDocumentKind.Return"/>; the type exists so a return is stored apart from
/// the sales, as the business's own ledgers always kept it. A return is created through
/// <see cref="SalesInvoice.CreateDraft"/>, which picks this type for the kind.
/// </para>
/// <para>
/// Its lines, their tax and serial numbers, and its charges are the return's own types for
/// the same reason, and are made by the return itself.
/// </para>
/// </remarks>
public sealed class SalesReturn : SalesInvoice
{
    internal SalesReturn(
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
            SalesDocumentKind.Return,
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
    private SalesReturn()
    {
    }
}

/// <summary>A line of a sales return: the goods coming back.</summary>
public sealed class SalesReturnLine : SalesInvoiceLine
{
    internal SalesReturnLine(
        SalesInvoiceLineId id,
        TenantId tenantId,
        SalesInvoiceId returnId,
        ProductId productId,
        BatchId? batchId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        decimal discount,
        TaxAssessment assessment,
        int lineNumber)
        : base(id, tenantId, returnId, productId, batchId, unitId, quantity, stockQuantity, rate, discount, assessment, lineNumber, returned: true)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesReturnLine()
    {
    }

    /// <inheritdoc />
    private protected override SalesInvoiceLineSerial NewSerial(SerialNumberId serialId) =>
        new SalesReturnLineSerial(TenantId, Id, serialId);
}

/// <summary>A serial number on a line of a sales return.</summary>
public sealed class SalesReturnLineSerial : SalesInvoiceLineSerial
{
    internal SalesReturnLineSerial(TenantId tenantId, SalesInvoiceLineId lineId, SerialNumberId serialNumberId)
        : base(tenantId, lineId, serialNumberId)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesReturnLineSerial()
    {
    }
}

/// <summary>A tax component on a line of a sales return.</summary>
public sealed class SalesReturnLineTax : SalesInvoiceLineTax
{
    internal SalesReturnLineTax(
        TenantId tenantId,
        SalesInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
        : base(tenantId, lineId, type, percentage, amount)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesReturnLineTax()
    {
    }
}

/// <summary>A charge on a sales return: its additional table.</summary>
public sealed class SalesReturnCharge : SalesInvoiceCharge
{
    internal SalesReturnCharge(
        SalesInvoiceChargeId id,
        TenantId tenantId,
        SalesInvoiceId returnId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id, tenantId, returnId, ledgerId, amount, isAddition)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesReturnCharge()
    {
    }
}
