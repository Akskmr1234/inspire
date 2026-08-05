using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERP.Infrastructure.Persistence.Conversion;

/// <summary>Converts <see cref="AccountGroupId"/> to and from <see cref="Guid"/>.</summary>
public sealed class AccountGroupIdConverter : ValueConverter<AccountGroupId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="AccountGroupIdConverter"/> class.</summary>
    public AccountGroupIdConverter()
        : base(id => id.Value, value => AccountGroupId.From(value))
    {
    }
}

/// <summary>Converts <see cref="LedgerId"/> to and from <see cref="Guid"/>.</summary>
public sealed class LedgerIdConverter : ValueConverter<LedgerId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="LedgerIdConverter"/> class.</summary>
    public LedgerIdConverter()
        : base(id => id.Value, value => LedgerId.From(value))
    {
    }
}

/// <summary>Converts <see cref="VoucherId"/> to and from <see cref="Guid"/>.</summary>
public sealed class VoucherIdConverter : ValueConverter<VoucherId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="VoucherIdConverter"/> class.</summary>
    public VoucherIdConverter()
        : base(id => id.Value, value => VoucherId.From(value))
    {
    }
}

/// <summary>Converts <see cref="VoucherLineId"/> to and from <see cref="Guid"/>.</summary>
public sealed class VoucherLineIdConverter : ValueConverter<VoucherLineId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="VoucherLineIdConverter"/> class.</summary>
    public VoucherLineIdConverter()
        : base(id => id.Value, value => VoucherLineId.From(value))
    {
    }
}
