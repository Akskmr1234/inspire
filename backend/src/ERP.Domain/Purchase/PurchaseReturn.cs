using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>
/// Goods sent back to a supplier: a purchase invoice of kind <see cref="PurchaseDocumentKind.Return"/>,
/// kept in the Purchase Return master, details and additional tables.
/// </summary>
/// <remarks>
/// <para>
/// Everything a return does it does as a <see cref="PurchaseInvoice"/>; the type exists so a
/// purchase and a return are each stored in tables of their own, as the business's own
/// ledgers always kept them. A return is created through
/// <see cref="PurchaseInvoice.CreateDraft"/>, which picks this type for the kind.
/// </para>
/// <para>
/// Its lines, their tax and serial numbers, and its charges are the return's own types for the
/// same reason, and are made by the document itself.
/// </para>
/// </remarks>
public sealed class PurchaseReturn : PurchaseInvoice
{
    internal PurchaseReturn(
        PurchaseInvoiceId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        LedgerId supplierLedgerId,
        WarehouseId warehouseId,
        TaxMode mode,
        CurrencyCode currency,
        PurchaseInvoiceId? returnsInvoiceId,
        PurchaseOrderId? purchaseOrderId)
        : base(
            id,
            tenantId,
            firmId,
            branchId,
            financialYearId,
            PurchaseDocumentKind.Return,
            number,
            date,
            supplierLedgerId,
            warehouseId,
            mode,
            currency,
            returnsInvoiceId,
            purchaseOrderId)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseReturn()
    {
    }
}

/// <summary>A line of a purchase return: the goods going back.</summary>
public sealed class PurchaseReturnLine : PurchaseInvoiceLine
{
    internal PurchaseReturnLine(
        PurchaseInvoiceLineId id,
        TenantId tenantId,
        PurchaseInvoiceId invoiceId,
        ProductId productId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        decimal discount,
        TaxAssessment assessment,
        int lineNumber,
        string? batchNumber,
        DateOnly? expiresOn,
        decimal? sellingRate)
        : base(
            id, tenantId, invoiceId, productId, unitId, quantity, stockQuantity, rate, discount, assessment,
            lineNumber, batchNumber, expiresOn, sellingRate, returned: true)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseReturnLine()
    {
    }

    /// <inheritdoc />
    private protected override PurchaseInvoiceLineSerial NewSerial(string serialNumber) =>
        new PurchaseReturnLineSerial(TenantId, Id, serialNumber);
}

/// <summary>A serial number on a line of a purchase return.</summary>
public sealed class PurchaseReturnLineSerial : PurchaseInvoiceLineSerial
{
    internal PurchaseReturnLineSerial(TenantId tenantId, PurchaseInvoiceLineId lineId, string serialNumber)
        : base(tenantId, lineId, serialNumber)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseReturnLineSerial()
    {
    }
}

/// <summary>A tax component on a line of a purchase return.</summary>
public sealed class PurchaseReturnLineTax : PurchaseInvoiceLineTax
{
    internal PurchaseReturnLineTax(
        TenantId tenantId,
        PurchaseInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
        : base(tenantId, lineId, type, percentage, amount)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseReturnLineTax()
    {
    }
}

/// <summary>A charge on a purchase return: its additional table.</summary>
public sealed class PurchaseReturnCharge : PurchaseInvoiceCharge
{
    internal PurchaseReturnCharge(
        PurchaseInvoiceChargeId id,
        TenantId tenantId,
        PurchaseInvoiceId invoiceId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id, tenantId, invoiceId, ledgerId, amount, isAddition)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseReturnCharge()
    {
    }
}
