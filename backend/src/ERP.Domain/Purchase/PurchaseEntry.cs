using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>
/// Goods bought from a supplier: a purchase invoice of kind <see cref="PurchaseDocumentKind.Invoice"/>,
/// kept in the Purchase master, details and additional tables.
/// </summary>
/// <remarks>
/// <para>
/// Everything a purchase does it does as a <see cref="PurchaseInvoice"/>; the type exists so a
/// purchase and a return are each stored in tables of their own, as the business's own
/// ledgers always kept them. A purchase is created through
/// <see cref="PurchaseInvoice.CreateDraft"/>, which picks this type for the kind.
/// </para>
/// <para>
/// Its lines, their tax and serial numbers, and its charges are the purchase's own types for the
/// same reason, and are made by the document itself.
/// </para>
/// </remarks>
public sealed class PurchaseEntry : PurchaseInvoice
{
    internal PurchaseEntry(
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
            PurchaseDocumentKind.Invoice,
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
    private PurchaseEntry()
    {
    }
}

/// <summary>A line of a purchase: the goods coming in.</summary>
public sealed class PurchaseEntryLine : PurchaseInvoiceLine
{
    internal PurchaseEntryLine(
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
            lineNumber, batchNumber, expiresOn, sellingRate, returned: false)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseEntryLine()
    {
    }

    /// <inheritdoc />
    private protected override PurchaseInvoiceLineSerial NewSerial(string serialNumber) =>
        new PurchaseEntryLineSerial(TenantId, Id, serialNumber);
}

/// <summary>A serial number on a line of a purchase.</summary>
public sealed class PurchaseEntryLineSerial : PurchaseInvoiceLineSerial
{
    internal PurchaseEntryLineSerial(TenantId tenantId, PurchaseInvoiceLineId lineId, string serialNumber)
        : base(tenantId, lineId, serialNumber)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseEntryLineSerial()
    {
    }
}

/// <summary>A tax component on a line of a purchase.</summary>
public sealed class PurchaseEntryLineTax : PurchaseInvoiceLineTax
{
    internal PurchaseEntryLineTax(
        TenantId tenantId,
        PurchaseInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
        : base(tenantId, lineId, type, percentage, amount)
    {
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseEntryLineTax()
    {
    }
}

/// <summary>A charge on a purchase: its additional table.</summary>
public sealed class PurchaseEntryCharge : PurchaseInvoiceCharge
{
    internal PurchaseEntryCharge(
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
    private PurchaseEntryCharge()
    {
    }
}
