using ERP.Domain.Accounting;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Parties;

/// <summary>Identifies a country.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct CountryId(Guid Value) : IStronglyTypedId<CountryId>
{
    /// <inheritdoc />
    public static CountryId From(Guid value) => new(value);

    /// <inheritdoc />
    public static CountryId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a state, emirate or region within a country.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RegionId(Guid Value) : IStronglyTypedId<RegionId>
{
    /// <inheritdoc />
    public static RegionId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RegionId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies an area or a route.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct TerritoryId(Guid Value) : IStronglyTypedId<TerritoryId>
{
    /// <inheritdoc />
    public static TerritoryId From(Guid value) => new(value);

    /// <inheritdoc />
    public static TerritoryId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>The two ways a firm divides up where its customers are.</summary>
public enum TerritoryKind
{
    /// <summary>A part of town or a district a customer is in.</summary>
    Area = 1,

    /// <summary>The delivery or collection round a customer is on.</summary>
    Route = 2,
}

/// <summary>A country a firm's customers and suppliers may be in, with its currency.</summary>
/// <remarks>
/// Per firm, so a firm can correct a name or a currency and add what the seeded list
/// lacks. Known to the rest of the system by its code - the two-letter ISO code - which
/// is what a party records, so a country edited here does not have to be found again on
/// every customer.
/// </remarks>
public sealed class Country : AggregateRoot<CountryId>, IFirmScoped, IAuditable
{
    /// <summary>The longest code.</summary>
    public const int MaximumCodeLength = 3;

    /// <summary>The longest name.</summary>
    public const int MaximumNameLength = 100;

    /// <summary>The longest currency name.</summary>
    public const int MaximumCurrencyNameLength = 50;

    private Country(
        CountryId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        string? currencyCode)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        CurrencyCode = currencyCode;
        IsActive = true;
    }

    private Country()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the ISO code, unique within the firm: QA, AE, IN.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets the ISO code of the currency used there, where it has one of its own.</summary>
    public string? CurrencyCode { get; private set; }

    /// <summary>Gets the currency's name: Qatari Riyal.</summary>
    public string? CurrencyName { get; private set; }

    /// <summary>Gets whether the country is offered on new records.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Adds a country.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The ISO code.</param>
    /// <param name="name">The name.</param>
    /// <param name="currencyCode">The currency's ISO code, if it has one.</param>
    /// <param name="currencyName">The currency's name.</param>
    /// <returns>The country, or the reason it was refused.</returns>
    public static Result<Country> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        string? currencyCode,
        string? currencyName = null)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<Country>(Error.Validation(
                "Country.CodeInvalid",
                $"A country needs a code of at most {MaximumCodeLength} letters."));
        }

        Country country = new(
            CountryId.NewId(), tenantId, firmId, code.Trim().ToUpperInvariant(), string.Empty, null);

        Result changed = country.Change(name, null, currencyCode, currencyName);

        return changed.IsFailure
            ? Result.Failure<Country>(changed.Error)
            : Result.Success(country);
    }

    /// <summary>Corrects the country's names and currency.</summary>
    /// <param name="name">The name.</param>
    /// <param name="nameArabic">The name in Arabic.</param>
    /// <param name="currencyCode">The currency's ISO code, if it has one.</param>
    /// <param name="currencyName">The currency's name.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Change(string name, string? nameArabic, string? currencyCode, string? currencyName)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
        {
            return Result.Failure(Error.Validation(
                "Country.NameInvalid",
                $"A country needs a name of at most {MaximumNameLength} characters."));
        }

        string? currency = Trimmed(currencyCode)?.ToUpperInvariant();

        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetter)))
        {
            return Result.Failure(Error.Validation(
                "Country.CurrencyInvalid", "A currency code is three letters, such as QAR."));
        }

        if (currencyName is not null && currencyName.Trim().Length > MaximumCurrencyNameLength)
        {
            return Result.Failure(Error.Validation(
                "Country.CurrencyNameTooLong",
                $"A currency name cannot exceed {MaximumCurrencyNameLength} characters."));
        }

        Name = name.Trim();
        NameArabic = Trimmed(nameArabic);
        CurrencyCode = currency;
        CurrencyName = Trimmed(currencyName);

        return Result.Success();
    }

    /// <summary>Stops the country being offered on new records.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Offers the country again.</summary>
    public void Activate() => IsActive = true;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>A state, emirate or region of a country.</summary>
/// <remarks>
/// Known to a party by its code, which is unique within the firm: under GST it is the
/// two-digit code a GSTIN begins with, and it is that code a customer's place of supply
/// is compared with the firm's own by.
/// </remarks>
public sealed class Region : AggregateRoot<RegionId>, IFirmScoped, IAuditable
{
    /// <summary>The longest code, which is what a ledger's state code holds.</summary>
    public const int MaximumCodeLength = 10;

    /// <summary>The longest name.</summary>
    public const int MaximumNameLength = 100;

    private Region(
        RegionId id,
        TenantId tenantId,
        FirmId firmId,
        CountryId countryId,
        string code)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        CountryId = countryId;
        Code = code;
        Name = string.Empty;
        IsActive = true;
    }

    private Region()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the country it is in.</summary>
    public CountryId CountryId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets whether it is offered on new records.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Adds a state to a country.</summary>
    /// <param name="country">The country.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="nameArabic">The name in Arabic.</param>
    /// <returns>The state, or the reason it was refused.</returns>
    public static Result<Region> Create(Country country, string code, string name, string? nameArabic = null)
    {
        ArgumentNullException.ThrowIfNull(country);

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<Region>(Error.Validation(
                "Region.CodeInvalid",
                $"A state needs a code of at most {MaximumCodeLength} characters."));
        }

        Region region = new(
            RegionId.NewId(), country.TenantId, country.FirmId, country.Id,
            code.Trim().ToUpperInvariant());

        Result changed = region.Change(country, name, nameArabic);

        return changed.IsFailure ? Result.Failure<Region>(changed.Error) : Result.Success(region);
    }

    /// <summary>Corrects the state's names, or moves it to another country.</summary>
    /// <param name="country">The country it is in.</param>
    /// <param name="name">The name.</param>
    /// <param name="nameArabic">The name in Arabic.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Change(Country country, string name, string? nameArabic)
    {
        ArgumentNullException.ThrowIfNull(country);

        if (country.FirmId != FirmId)
        {
            return Result.Failure(Error.Validation(
                "Region.CountryInAnotherFirm", "That country belongs to another firm."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
        {
            return Result.Failure(Error.Validation(
                "Region.NameInvalid",
                $"A state needs a name of at most {MaximumNameLength} characters."));
        }

        CountryId = country.Id;
        Name = name.Trim();
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

        return Result.Success();
    }

    /// <summary>Stops the state being offered on new records.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Offers the state again.</summary>
    public void Activate() => IsActive = true;
}

/// <summary>An area or a route: how a firm divides up where its customers are.</summary>
public sealed class Territory : AggregateRoot<TerritoryId>, IFirmScoped, IAuditable
{
    /// <summary>The longest name.</summary>
    public const int MaximumNameLength = 100;

    private Territory(TerritoryId id, TenantId tenantId, FirmId firmId, TerritoryKind kind, string name)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Kind = kind;
        Name = name;
        IsActive = true;
    }

    private Territory() => Name = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets whether this is an area or a route.</summary>
    public TerritoryKind Kind { get; private set; }

    /// <summary>Gets the name, unique among the firm's areas or routes.</summary>
    public string Name { get; private set; }

    /// <summary>Gets whether it is offered on new records.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Adds an area or a route.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="kind">Area or route.</param>
    /// <param name="name">The name.</param>
    /// <returns>The territory, or the reason it was refused.</returns>
    public static Result<Territory> Create(TenantId tenantId, FirmId firmId, TerritoryKind kind, string name)
    {
        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<Territory>(Error.Validation(
                "Territory.KindInvalid", "A territory is an area or a route."));
        }

        Territory territory = new(TerritoryId.NewId(), tenantId, firmId, kind, string.Empty);
        Result renamed = territory.Rename(name);

        return renamed.IsFailure ? Result.Failure<Territory>(renamed.Error) : Result.Success(territory);
    }

    /// <summary>Renames the area or route.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength)
        {
            return Result.Failure(Error.Validation(
                "Territory.NameInvalid",
                $"An area or route needs a name of at most {MaximumNameLength} characters."));
        }

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Stops it being offered on new records.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Offers it again.</summary>
    public void Activate() => IsActive = true;
}

/// <summary>A party's photograph, kept apart from its ledger so a list of parties stays light.</summary>
/// <remarks>
/// Stored as a data URL of a picture the screen has already scaled down, which is all a
/// customer card or a printed statement needs. A party may have up to
/// <see cref="MaximumSlots"/>, each in a numbered slot; the first is the one a card shows.
/// </remarks>
public sealed class PartyPhoto : AggregateRoot<PartyPhotoId>, IFirmScoped
{
    /// <summary>The largest picture accepted, as the length of its data URL.</summary>
    public const int MaximumLength = 700_000;

    /// <summary>How many photographs a party may have.</summary>
    public const int MaximumSlots = 3;

    private PartyPhoto(
        PartyPhotoId id,
        TenantId tenantId,
        FirmId firmId,
        LedgerId ledgerId,
        int slot,
        string content)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        LedgerId = ledgerId;
        Slot = slot;
        Content = content;
    }

    private PartyPhoto() => Content = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the ledger of the party pictured.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets which of the party's photographs this is, from one.</summary>
    public int Slot { get; private set; } = 1;

    /// <summary>Gets the picture, as a data URL.</summary>
    public string Content { get; private set; }

    /// <summary>Records a party's photograph.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="ledgerId">The party's ledger.</param>
    /// <param name="content">The picture, as a data URL.</param>
    /// <param name="slot">Which of the party's photographs it is, from one.</param>
    /// <returns>The photograph, or the reason it was refused.</returns>
    public static Result<PartyPhoto> Create(
        TenantId tenantId,
        FirmId firmId,
        LedgerId ledgerId,
        string content,
        int slot = 1)
    {
        if (slot is < 1 or > MaximumSlots)
        {
            return Result.Failure<PartyPhoto>(Error.Validation(
                "PartyPhoto.SlotInvalid", $"A party has at most {MaximumSlots} photographs."));
        }

        PartyPhoto photo = new(PartyPhotoId.NewId(), tenantId, firmId, ledgerId, slot, string.Empty);
        Result replaced = photo.Replace(content);

        return replaced.IsFailure ? Result.Failure<PartyPhoto>(replaced.Error) : Result.Success(photo);
    }

    /// <summary>Replaces the picture.</summary>
    /// <param name="content">The picture, as a data URL.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Replace(string content)
    {
        if (string.IsNullOrWhiteSpace(content)
            || !content.StartsWith("data:image/", StringComparison.Ordinal)
            || content.Length > MaximumLength)
        {
            return Result.Failure(Error.Validation(
                "PartyPhoto.Invalid",
                "A photograph is a picture of at most about half a megabyte."));
        }

        Content = content;
        return Result.Success();
    }
}

/// <summary>Identifies a party's photograph.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PartyPhotoId(Guid Value) : IStronglyTypedId<PartyPhotoId>
{
    /// <inheritdoc />
    public static PartyPhotoId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PartyPhotoId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
