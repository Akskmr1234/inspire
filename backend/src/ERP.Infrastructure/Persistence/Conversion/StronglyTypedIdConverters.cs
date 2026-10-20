using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Parties;
using ERP.Domain.Platform;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERP.Infrastructure.Persistence.Conversion;

/// <summary>
/// Value converters mapping domain value objects onto database primitives.
/// </summary>
/// <remarks>
/// Registered once in <c>ConfigureConventions</c> so every property of a given
/// type is converted automatically. Doing this per-property instead would mean
/// remembering it on every entity, and the failure mode of forgetting - EF Core
/// refusing to map the type at all - only shows up when that entity is first
/// queried.
/// </remarks>
public sealed class TenantIdConverter : ValueConverter<TenantId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="TenantIdConverter"/> class.</summary>
    public TenantIdConverter()
        : base(id => id.Value, value => TenantId.From(value))
    {
    }
}

/// <summary>Converts <see cref="FirmId"/> to and from <see cref="Guid"/>.</summary>
public sealed class FirmIdConverter : ValueConverter<FirmId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="FirmIdConverter"/> class.</summary>
    public FirmIdConverter()
        : base(id => id.Value, value => FirmId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BranchId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BranchIdConverter : ValueConverter<BranchId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BranchIdConverter"/> class.</summary>
    public BranchIdConverter()
        : base(id => id.Value, value => BranchId.From(value))
    {
    }
}

/// <summary>Converts <see cref="UserId"/> to and from <see cref="Guid"/>.</summary>
public sealed class UserIdConverter : ValueConverter<UserId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="UserIdConverter"/> class.</summary>
    public UserIdConverter()
        : base(id => id.Value, value => UserId.From(value))
    {
    }
}

/// <summary>Converts <see cref="FinancialYearId"/> to and from <see cref="Guid"/>.</summary>
public sealed class FinancialYearIdConverter : ValueConverter<FinancialYearId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="FinancialYearIdConverter"/> class.</summary>
    public FinancialYearIdConverter()
        : base(id => id.Value, value => FinancialYearId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RoleId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RoleIdConverter : ValueConverter<RoleId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RoleIdConverter"/> class.</summary>
    public RoleIdConverter()
        : base(id => id.Value, value => RoleId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PermissionId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PermissionIdConverter : ValueConverter<PermissionId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PermissionIdConverter"/> class.</summary>
    public PermissionIdConverter()
        : base(id => id.Value, value => PermissionId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RefreshTokenId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RefreshTokenIdConverter : ValueConverter<RefreshTokenId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RefreshTokenIdConverter"/> class.</summary>
    public RefreshTokenIdConverter()
        : base(id => id.Value, value => RefreshTokenId.From(value))
    {
    }
}

/// <summary>
/// Converts <see cref="CurrencyCode"/> to and from its three-letter ISO 4217 code.
/// </summary>
/// <remarks>
/// Stored as the code itself rather than a surrogate key. A currency code is
/// already a short, stable, globally-agreed identifier, and storing it directly
/// makes ad-hoc SQL and report queries readable without a join.
/// </remarks>
public sealed class CurrencyCodeConverter : ValueConverter<CurrencyCode, string>
{
    /// <summary>Initialises a new instance of the <see cref="CurrencyCodeConverter"/> class.</summary>
    public CurrencyCodeConverter()
        : base(currency => currency.Code, code => CurrencyCode.FromTrusted(code))
    {
    }
}

/// <summary>Converts <see cref="BillId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BillIdConverter : ValueConverter<BillId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BillIdConverter"/> class.</summary>
    public BillIdConverter()
        : base(id => id.Value, value => BillId.From(value))
    {
    }
}

/// <summary>Converts <see cref="ChequeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class ChequeIdConverter : ValueConverter<ChequeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="ChequeIdConverter"/> class.</summary>
    public ChequeIdConverter()
        : base(id => id.Value, value => ChequeId.From(value))
    {
    }
}

/// <summary>Converts <see cref="MenuItemId"/> to and from <see cref="Guid"/>.</summary>
public sealed class MenuItemIdConverter : ValueConverter<MenuItemId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="MenuItemIdConverter"/> class.</summary>
    public MenuItemIdConverter()
        : base(id => id.Value, value => MenuItemId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BillAllocationId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BillAllocationIdConverter : ValueConverter<BillAllocationId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BillAllocationIdConverter"/> class.</summary>
    public BillAllocationIdConverter()
        : base(id => id.Value, value => BillAllocationId.From(value))
    {
    }
}

/// <summary>Converts <see cref="FirmSettingId"/> to and from <see cref="Guid"/>.</summary>
public sealed class FirmSettingIdConverter : ValueConverter<FirmSettingId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="FirmSettingIdConverter"/> class.</summary>
    public FirmSettingIdConverter()
        : base(id => id.Value, value => FirmSettingId.From(value))
    {
    }
}

/// <summary>Converts <see cref="GridLayoutId"/> to and from <see cref="Guid"/>.</summary>
public sealed class GridLayoutIdConverter : ValueConverter<GridLayoutId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="GridLayoutIdConverter"/> class.</summary>
    public GridLayoutIdConverter()
        : base(id => id.Value, value => GridLayoutId.From(value))
    {
    }
}

/// <summary>Converts <see cref="DashboardId"/> to and from <see cref="Guid"/>.</summary>
public sealed class DashboardIdConverter : ValueConverter<DashboardId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="DashboardIdConverter"/> class.</summary>
    public DashboardIdConverter()
        : base(id => id.Value, value => DashboardId.From(value))
    {
    }
}

/// <summary>Converts <see cref="DashboardWidgetId"/> to and from <see cref="Guid"/>.</summary>
public sealed class DashboardWidgetIdConverter : ValueConverter<DashboardWidgetId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="DashboardWidgetIdConverter"/> class.</summary>
    public DashboardWidgetIdConverter()
        : base(id => id.Value, value => DashboardWidgetId.From(value))
    {
    }
}

/// <summary>Converts <see cref="UnitOfMeasureId"/> to and from <see cref="Guid"/>.</summary>
public sealed class UnitOfMeasureIdConverter : ValueConverter<UnitOfMeasureId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="UnitOfMeasureIdConverter"/> class.</summary>
    public UnitOfMeasureIdConverter()
        : base(id => id.Value, value => UnitOfMeasureId.From(value))
    {
    }
}

/// <summary>Converts <see cref="CategoryId"/> to and from <see cref="Guid"/>.</summary>
public sealed class CategoryIdConverter : ValueConverter<CategoryId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="CategoryIdConverter"/> class.</summary>
    public CategoryIdConverter()
        : base(id => id.Value, value => CategoryId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BrandId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BrandIdConverter : ValueConverter<BrandId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BrandIdConverter"/> class.</summary>
    public BrandIdConverter()
        : base(id => id.Value, value => BrandId.From(value))
    {
    }
}

/// <summary>Converts <see cref="WarehouseId"/> to and from <see cref="Guid"/>.</summary>
public sealed class ProductIdConverter : ValueConverter<ProductId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="ProductIdConverter"/> class.</summary>
    public ProductIdConverter()
        : base(id => id.Value, value => ProductId.From(value))
    {
    }
}

/// <summary>Converts <see cref="ProductBarcodeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class ProductBarcodeIdConverter : ValueConverter<ProductBarcodeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="ProductBarcodeIdConverter"/> class.</summary>
    public ProductBarcodeIdConverter()
        : base(id => id.Value, value => ProductBarcodeId.From(value))
    {
    }
}

/// <summary>Converts <see cref="WarehouseId"/> to and from <see cref="Guid"/>.</summary>
public sealed class WarehouseIdConverter : ValueConverter<WarehouseId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="WarehouseIdConverter"/> class.</summary>
    public WarehouseIdConverter()
        : base(id => id.Value, value => WarehouseId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockDocumentId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockDocumentIdConverter : ValueConverter<StockDocumentId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockDocumentIdConverter"/> class.</summary>
    public StockDocumentIdConverter()
        : base(id => id.Value, value => StockDocumentId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockDocumentLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockDocumentLineIdConverter : ValueConverter<StockDocumentLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockDocumentLineIdConverter"/> class.</summary>
    public StockDocumentLineIdConverter()
        : base(id => id.Value, value => StockDocumentLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockBalanceId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockBalanceIdConverter : ValueConverter<StockBalanceId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockBalanceIdConverter"/> class.</summary>
    public StockBalanceIdConverter()
        : base(id => id.Value, value => StockBalanceId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockLedgerEntryId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockLedgerEntryIdConverter : ValueConverter<StockLedgerEntryId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockLedgerEntryIdConverter"/> class.</summary>
    public StockLedgerEntryIdConverter()
        : base(id => id.Value, value => StockLedgerEntryId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BatchId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BatchIdConverter : ValueConverter<BatchId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BatchIdConverter"/> class.</summary>
    public BatchIdConverter()
        : base(id => id.Value, value => BatchId.From(value))
    {
    }
}

/// <summary>Converts <see cref="InventoryAccountMapId"/> to and from <see cref="Guid"/>.</summary>
public sealed class InventoryAccountMapIdConverter : ValueConverter<InventoryAccountMapId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="InventoryAccountMapIdConverter"/> class.</summary>
    public InventoryAccountMapIdConverter()
        : base(id => id.Value, value => InventoryAccountMapId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SerialNumberId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SerialNumberIdConverter : ValueConverter<SerialNumberId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SerialNumberIdConverter"/> class.</summary>
    public SerialNumberIdConverter()
        : base(id => id.Value, value => SerialNumberId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BatchBalanceId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BatchBalanceIdConverter : ValueConverter<BatchBalanceId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BatchBalanceIdConverter"/> class.</summary>
    public BatchBalanceIdConverter()
        : base(id => id.Value, value => BatchBalanceId.From(value))
    {
    }
}

/// <summary>Converts <see cref="AdditionalLedgerId"/> to and from <see cref="Guid"/>.</summary>
public sealed class AdditionalLedgerIdConverter : ValueConverter<AdditionalLedgerId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="AdditionalLedgerIdConverter"/> class.</summary>
    public AdditionalLedgerIdConverter()
        : base(id => id.Value, value => AdditionalLedgerId.From(value))
    {
    }
}

/// <summary>Converts <see cref="TaxAccountMapId"/> to and from <see cref="Guid"/>.</summary>
public sealed class TaxAccountMapIdConverter : ValueConverter<TaxAccountMapId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="TaxAccountMapIdConverter"/> class.</summary>
    public TaxAccountMapIdConverter()
        : base(id => id.Value, value => TaxAccountMapId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesInvoiceId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesInvoiceIdConverter : ValueConverter<SalesInvoiceId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesInvoiceIdConverter"/> class.</summary>
    public SalesInvoiceIdConverter()
        : base(id => id.Value, value => SalesInvoiceId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesInvoiceLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesInvoiceLineIdConverter : ValueConverter<SalesInvoiceLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesInvoiceLineIdConverter"/> class.</summary>
    public SalesInvoiceLineIdConverter()
        : base(id => id.Value, value => SalesInvoiceLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesInvoiceChargeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesInvoiceChargeIdConverter : ValueConverter<SalesInvoiceChargeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesInvoiceChargeIdConverter"/> class.</summary>
    public SalesInvoiceChargeIdConverter()
        : base(id => id.Value, value => SalesInvoiceChargeId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesOrderId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesOrderIdConverter : ValueConverter<SalesOrderId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesOrderIdConverter"/> class.</summary>
    public SalesOrderIdConverter()
        : base(id => id.Value, value => SalesOrderId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesOrderLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesOrderLineIdConverter : ValueConverter<SalesOrderLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesOrderLineIdConverter"/> class.</summary>
    public SalesOrderLineIdConverter()
        : base(id => id.Value, value => SalesOrderLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="SalesOrderChargeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class SalesOrderChargeIdConverter : ValueConverter<SalesOrderChargeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="SalesOrderChargeIdConverter"/> class.</summary>
    public SalesOrderChargeIdConverter()
        : base(id => id.Value, value => SalesOrderChargeId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseInvoiceId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseInvoiceIdConverter : ValueConverter<PurchaseInvoiceId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseInvoiceIdConverter"/> class.</summary>
    public PurchaseInvoiceIdConverter()
        : base(id => id.Value, value => PurchaseInvoiceId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseInvoiceLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseInvoiceLineIdConverter : ValueConverter<PurchaseInvoiceLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseInvoiceLineIdConverter"/> class.</summary>
    public PurchaseInvoiceLineIdConverter()
        : base(id => id.Value, value => PurchaseInvoiceLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseInvoiceChargeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseInvoiceChargeIdConverter
    : ValueConverter<PurchaseInvoiceChargeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseInvoiceChargeIdConverter"/> class.</summary>
    public PurchaseInvoiceChargeIdConverter()
        : base(id => id.Value, value => PurchaseInvoiceChargeId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseOrderId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseOrderIdConverter : ValueConverter<PurchaseOrderId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseOrderIdConverter"/> class.</summary>
    public PurchaseOrderIdConverter()
        : base(id => id.Value, value => PurchaseOrderId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseOrderLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseOrderLineIdConverter : ValueConverter<PurchaseOrderLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseOrderLineIdConverter"/> class.</summary>
    public PurchaseOrderLineIdConverter()
        : base(id => id.Value, value => PurchaseOrderLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="PurchaseOrderChargeId"/> to and from <see cref="Guid"/>.</summary>
public sealed class PurchaseOrderChargeIdConverter
    : ValueConverter<PurchaseOrderChargeId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PurchaseOrderChargeIdConverter"/> class.</summary>
    public PurchaseOrderChargeIdConverter()
        : base(id => id.Value, value => PurchaseOrderChargeId.From(value))
    {
    }
}

/// <summary>Stores a <see cref="TaxSlabId"/> as a GUID.</summary>
public sealed class TaxSlabIdConverter : ValueConverter<TaxSlabId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="TaxSlabIdConverter"/> class.</summary>
    public TaxSlabIdConverter()
        : base(id => id.Value, value => TaxSlabId.From(value))
    {
    }
}

/// <summary>Stores a <see cref="CountryId"/> as a GUID.</summary>
public sealed class CountryIdConverter : ValueConverter<CountryId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="CountryIdConverter"/> class.</summary>
    public CountryIdConverter()
        : base(id => id.Value, value => CountryId.From(value))
    {
    }
}

/// <summary>Stores a <see cref="RegionId"/> as a GUID.</summary>
public sealed class RegionIdConverter : ValueConverter<RegionId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RegionIdConverter"/> class.</summary>
    public RegionIdConverter()
        : base(id => id.Value, value => RegionId.From(value))
    {
    }
}

/// <summary>Stores a <see cref="TerritoryId"/> as a GUID.</summary>
public sealed class TerritoryIdConverter : ValueConverter<TerritoryId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="TerritoryIdConverter"/> class.</summary>
    public TerritoryIdConverter()
        : base(id => id.Value, value => TerritoryId.From(value))
    {
    }
}

/// <summary>Stores a <see cref="PartyPhotoId"/> as a GUID.</summary>
public sealed class PartyPhotoIdConverter : ValueConverter<PartyPhotoId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="PartyPhotoIdConverter"/> class.</summary>
    public PartyPhotoIdConverter()
        : base(id => id.Value, value => PartyPhotoId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockTransferRequestId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockTransferRequestIdConverter : ValueConverter<StockTransferRequestId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockTransferRequestIdConverter"/> class.</summary>
    public StockTransferRequestIdConverter()
        : base(id => id.Value, value => StockTransferRequestId.From(value))
    {
    }
}

/// <summary>Converts <see cref="StockTransferRequestLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class StockTransferRequestLineIdConverter
    : ValueConverter<StockTransferRequestLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="StockTransferRequestLineIdConverter"/> class.</summary>
    public StockTransferRequestLineIdConverter()
        : base(id => id.Value, value => StockTransferRequestLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BillOfMaterialsId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BillOfMaterialsIdConverter : ValueConverter<BillOfMaterialsId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BillOfMaterialsIdConverter"/> class.</summary>
    public BillOfMaterialsIdConverter()
        : base(id => id.Value, value => BillOfMaterialsId.From(value))
    {
    }
}

/// <summary>Converts <see cref="BillOfMaterialsLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class BillOfMaterialsLineIdConverter : ValueConverter<BillOfMaterialsLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="BillOfMaterialsLineIdConverter"/> class.</summary>
    public BillOfMaterialsLineIdConverter()
        : base(id => id.Value, value => BillOfMaterialsLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="JobCardId"/> to and from <see cref="Guid"/>.</summary>
public sealed class JobCardIdConverter : ValueConverter<JobCardId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="JobCardIdConverter"/> class.</summary>
    public JobCardIdConverter()
        : base(id => id.Value, value => JobCardId.From(value))
    {
    }
}

/// <summary>Converts <see cref="JobCardLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class JobCardLineIdConverter : ValueConverter<JobCardLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="JobCardLineIdConverter"/> class.</summary>
    public JobCardLineIdConverter()
        : base(id => id.Value, value => JobCardLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RmaId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RmaIdConverter : ValueConverter<RmaId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RmaIdConverter"/> class.</summary>
    public RmaIdConverter()
        : base(id => id.Value, value => RmaId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RmaLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RmaLineIdConverter : ValueConverter<RmaLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RmaLineIdConverter"/> class.</summary>
    public RmaLineIdConverter()
        : base(id => id.Value, value => RmaLineId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RmaPhotoId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RmaPhotoIdConverter : ValueConverter<RmaPhotoId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RmaPhotoIdConverter"/> class.</summary>
    public RmaPhotoIdConverter()
        : base(id => id.Value, value => RmaPhotoId.From(value))
    {
    }
}

/// <summary>Converts <see cref="RmaEventId"/> to and from <see cref="Guid"/>.</summary>
public sealed class RmaEventIdConverter : ValueConverter<RmaEventId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="RmaEventIdConverter"/> class.</summary>
    public RmaEventIdConverter()
        : base(id => id.Value, value => RmaEventId.From(value))
    {
    }
}
