using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Identity;

/// <summary>The kinds of second factor a user can enrol.</summary>
public enum MfaFactorKind
{
    /// <summary>A six-digit code from an authenticator app (RFC 6238).</summary>
    Authenticator = 1,

    /// <summary>Approve or deny a sign-in on a phone.</summary>
    Push = 2,

    /// <summary>Single-use codes printed or saved for when the phone is lost.</summary>
    BackupCodes = 3,
}

/// <summary>Identifies an enrolled second factor.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct MfaFactorId(Guid Value) : IStronglyTypedId<MfaFactorId>
{
    /// <inheritdoc />
    public static MfaFactorId From(Guid value) => new(value);

    /// <inheritdoc />
    public static MfaFactorId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>A second factor a user has enrolled for signing in.</summary>
/// <remarks>
/// The secret itself never lives here. It is held by the second-factor server, which
/// knows the factor by its <see cref="Serial"/>; this row only says whose it is, what
/// kind it is, and whether enrolment finished. Reading this table therefore yields
/// nothing that could pass a second-factor check.
/// </remarks>
public sealed class MfaFactor : AggregateRoot<MfaFactorId>, ITenantScoped
{
    /// <summary>The longest label accepted.</summary>
    public const int MaximumLabelLength = 100;

    /// <summary>The longest serial accepted.</summary>
    public const int MaximumSerialLength = 60;

    private MfaFactor(
        MfaFactorId id,
        TenantId tenantId,
        UserId userId,
        MfaFactorKind kind,
        string serial,
        string label,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Kind = kind;
        Serial = serial;
        Label = label;
        CreatedAtUtc = createdAtUtc;
    }

    private MfaFactor()
    {
        Serial = string.Empty;
        Label = string.Empty;
    }

    /// <summary>Gets the owning tenant.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the user it signs in.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets what kind of factor it is.</summary>
    public MfaFactorKind Kind { get; private set; }

    /// <summary>Gets the serial the second-factor server knows it by.</summary>
    public string Serial { get; private set; }

    /// <summary>Gets the name the user gave it, such as "Work phone".</summary>
    public string Label { get; private set; }

    /// <summary>Gets when enrolment began.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Gets when enrolment finished; null while it is still being set up.</summary>
    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    /// <summary>Gets when it last passed a sign-in.</summary>
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    /// <summary>Gets a value indicating whether it is asked for at sign-in.</summary>
    public bool IsActive => ActivatedAtUtc is not null;

    /// <summary>Records a factor whose enrolment has begun.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="userId">The user it signs in.</param>
    /// <param name="kind">What kind of factor it is.</param>
    /// <param name="serial">The serial the second-factor server issued.</param>
    /// <param name="label">The user's name for it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The factor, or why it cannot be recorded.</returns>
    public static Result<MfaFactor> Begin(
        TenantId tenantId,
        UserId userId,
        MfaFactorKind kind,
        string serial,
        string label,
        DateTimeOffset nowUtc)
    {
        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<MfaFactor>(Error.Validation(
                "Mfa.UnknownKind", "That kind of second factor is not supported."));
        }

        if (string.IsNullOrWhiteSpace(serial) || serial.Trim().Length > MaximumSerialLength)
        {
            return Result.Failure<MfaFactor>(Error.Validation(
                "Mfa.SerialRequired", "The second-factor server returned no usable serial."));
        }

        string name = (label ?? string.Empty).Trim();

        if (name.Length > MaximumLabelLength)
        {
            return Result.Failure<MfaFactor>(Error.Validation(
                "Mfa.LabelTooLong", $"A name is at most {MaximumLabelLength} characters."));
        }

        return Result.Success(new MfaFactor(
            MfaFactorId.NewId(), tenantId, userId, kind, serial.Trim(), name, nowUtc));
    }

    /// <summary>Marks enrolment finished, so sign-in asks for it from now on.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void Activate(DateTimeOffset nowUtc) => ActivatedAtUtc ??= nowUtc;

    /// <summary>Records that it passed a sign-in.</summary>
    /// <param name="nowUtc">The current instant.</param>
    public void RecordUse(DateTimeOffset nowUtc) => LastUsedAtUtc = nowUtc;
}
