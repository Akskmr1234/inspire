using ERP.Domain.Accounting;
using ERP.Domain.Identity;
using ERP.Domain.Inventory;
using ERP.Domain.Platform;
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
