using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Platform;

/// <summary>Strongly-typed identifier for a <see cref="FirmSetting"/>.</summary>
/// <param name="Value">The underlying GUID.</param>
public readonly record struct FirmSettingId(Guid Value) : IStronglyTypedId<FirmSettingId>
{
    /// <inheritdoc />
    public static FirmSettingId From(Guid value) => new(value);

    /// <summary>Creates a new, time-ordered identifier.</summary>
    /// <returns>A new identifier.</returns>
    public static FirmSettingId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One named block of a firm's configuration, held as a document the client owns.
/// </summary>
/// <remarks>
/// <para>
/// The screens' preferences - which features are switched on, what a new document
/// starts with, how each kind of document prints - were kept in each browser. A firm
/// with three counters set them up three times, and the counter that had not been set
/// up printed on the wrong template and offered features the others had switched off.
/// Held here, per firm, they are set once and every workstation reads the same answer.
/// </para>
/// <para>
/// The server stores the document and does not interpret it, the same bargain the
/// grid layouts make: these settings decide how the screens behave and never what a
/// posted document means, so there is nothing for the server to enforce in them.
/// </para>
/// </remarks>
public sealed class FirmSetting : AggregateRoot<FirmSettingId>, IFirmScoped, IAuditable
{
    /// <summary>The longest key accepted.</summary>
    public const int MaximumKeyLength = 64;

    /// <summary>
    /// The largest document accepted, in characters. A print template with its layout
    /// is the biggest thing stored here; a megabyte is far beyond any of them and still
    /// small enough that nobody mistakes this for file storage.
    /// </summary>
    public const int MaximumValueLength = 1_000_000;

    private FirmSetting(
        FirmSettingId id,
        TenantId tenantId,
        FirmId firmId,
        string key,
        string value)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Key = key;
        Value = value;
    }

    private FirmSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the firm the setting belongs to.</summary>
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the name of the block, such as <c>preferences</c> or <c>print</c>.</summary>
    public string Key { get; private set; }

    /// <summary>Gets the document, as the client wrote it.</summary>
    public string Value { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a setting.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The firm it belongs to.</param>
    /// <param name="key">Its name.</param>
    /// <param name="value">The document.</param>
    /// <returns>The setting, or a validation failure.</returns>
    public static Result<FirmSetting> Create(
        TenantId tenantId,
        FirmId firmId,
        string key,
        string value)
    {
        Result keyCheck = ValidateKey(key);

        if (keyCheck.IsFailure)
        {
            return Result.Failure<FirmSetting>(keyCheck.Error);
        }

        Result valueCheck = ValidateValue(value);

        return valueCheck.IsFailure
            ? Result.Failure<FirmSetting>(valueCheck.Error)
            : Result.Success(new FirmSetting(
                FirmSettingId.NewId(), tenantId, firmId, Normalise(key), value));
    }

    /// <summary>Normalises a key to the form it is stored and looked up under.</summary>
    /// <param name="key">The key as given.</param>
    /// <returns>The key, trimmed and lower-cased.</returns>
    public static string Normalise(string key) =>
        (key ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Checks a key is one the store accepts.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Success, or a validation failure.</returns>
    /// <remarks>
    /// Lower-case letters, digits and hyphens: it appears in a URL, and a key that
    /// needed escaping there is a key nobody would type correctly twice.
    /// </remarks>
    public static Result ValidateKey(string key)
    {
        string normalised = Normalise(key);

        if (normalised.Length == 0)
        {
            return Result.Failure(Error.Validation(
                "FirmSetting.KeyRequired", "A setting needs a name."));
        }

        if (normalised.Length > MaximumKeyLength)
        {
            return Result.Failure(Error.Validation(
                "FirmSetting.KeyTooLong",
                $"A setting name cannot exceed {MaximumKeyLength} characters."));
        }

        return normalised.All(character =>
            character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            ? Result.Success()
            : Result.Failure(Error.Validation(
                "FirmSetting.KeyInvalid",
                "A setting name may hold only letters, digits and hyphens."));
    }

    /// <summary>Replaces the document.</summary>
    /// <param name="value">The new document.</param>
    /// <returns>Success, or a validation failure.</returns>
    public Result Replace(string value)
    {
        Result check = ValidateValue(value);

        if (check.IsFailure)
        {
            return check;
        }

        Value = value;

        return Result.Success();
    }

    private static Result ValidateValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure(Error.Validation(
                "FirmSetting.ValueRequired", "A setting must hold something."));
        }

        return value.Length > MaximumValueLength
            ? Result.Failure(Error.Validation(
                "FirmSetting.ValueTooLong",
                $"A setting cannot exceed {MaximumValueLength} characters."))
            : Result.Success();
    }
}
