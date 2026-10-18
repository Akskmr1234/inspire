using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>A tax percentage a product can carry - VAT 5%, Exempt 0%.</summary>
/// <remarks>
/// <para>
/// A master rather than a number typed on each product, so a change of rate is made once
/// and every product carrying it follows, and so the rates a firm charges are a short list
/// it chose rather than whatever somebody once keyed in.
/// </para>
/// <para>
/// One of a firm's rates may be its default: the one a new product starts with.
/// </para>
/// </remarks>
public sealed class TaxSlab : AggregateRoot<TaxSlabId>, IFirmScoped, IAuditable
{
    /// <summary>The longest code.</summary>
    public const int MaximumCodeLength = 30;

    /// <summary>The longest name.</summary>
    public const int MaximumNameLength = 100;

    private TaxSlab(
        TaxSlabId id,
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        decimal percentage)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Code = code;
        Name = name;
        Percentage = percentage;
        IsActive = true;
    }

    private TaxSlab()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the code, unique within the firm.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name, for example "VAT 5%".</summary>
    public string Name { get; private set; }

    /// <summary>Gets the name in Arabic.</summary>
    public string? NameArabic { get; private set; }

    /// <summary>Gets the percentage, for example 5 for 5%.</summary>
    public decimal Percentage { get; private set; }

    /// <summary>Gets whether new products start with this rate.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>Gets whether the rate may be given to products.</summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Creates a rate.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The name.</param>
    /// <param name="percentage">The percentage.</param>
    /// <returns>The rate, or the reason it was refused.</returns>
    public static Result<TaxSlab> Create(
        TenantId tenantId,
        FirmId firmId,
        string code,
        string name,
        decimal percentage)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > MaximumCodeLength)
        {
            return Result.Failure<TaxSlab>(Error.Validation(
                "TaxSlab.CodeInvalid",
                $"A tax rate needs a code of at most {MaximumCodeLength} characters."));
        }

        Result named = ValidateName(name);

        if (named.IsFailure)
        {
            return Result.Failure<TaxSlab>(named.Error);
        }

        Result<TaxRate> rate = TaxRate.Create(percentage);

        return rate.IsFailure
            ? Result.Failure<TaxSlab>(rate.Error)
            : Result.Success(new TaxSlab(
                TaxSlabId.NewId(), tenantId, firmId,
                code.Trim().ToUpperInvariant(), name.Trim(), rate.Value.Percentage));
    }

    /// <summary>Renames the rate and restates its percentage.</summary>
    /// <param name="name">The name.</param>
    /// <param name="percentage">The percentage.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A new percentage applies to documents raised from now on. Invoices already saved
    /// carry the rate they were raised at on their own lines.
    /// </remarks>
    public Result Change(string name, decimal percentage)
    {
        Result named = ValidateName(name);

        if (named.IsFailure)
        {
            return named;
        }

        Result<TaxRate> rate = TaxRate.Create(percentage);

        if (rate.IsFailure)
        {
            return Result.Failure(rate.Error);
        }

        Name = name.Trim();
        Percentage = rate.Value.Percentage;
        return Result.Success();
    }

    /// <summary>Sets the name in Arabic.</summary>
    /// <param name="nameArabic">The name, or null.</param>
    public void SetArabicName(string? nameArabic) =>
        NameArabic = string.IsNullOrWhiteSpace(nameArabic) ? null : nameArabic.Trim();

    /// <summary>Makes this the rate new products start with.</summary>
    /// <remarks>The caller clears the previous default in the same unit of work.</remarks>
    public void MakeDefault() => IsDefault = true;

    /// <summary>Stops this being the default.</summary>
    public void ClearDefault() => IsDefault = false;

    /// <summary>Stops the rate being given to products. A withdrawn rate is no default.</summary>
    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
    }

    /// <summary>Allows the rate to be given to products again.</summary>
    public void Activate() => IsActive = true;

    private static Result ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumNameLength
            ? Result.Failure(Error.Validation(
                "TaxSlab.NameInvalid",
                $"A tax rate needs a name of at most {MaximumNameLength} characters."))
            : Result.Success();
}
