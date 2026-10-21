using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Settings;

/// <summary>What happens when goods would leave a position below zero.</summary>
public enum NegativeStockPolicy
{
    /// <summary>Refused: nothing is issued that the system does not believe is there.</summary>
    Block = 1,

    /// <summary>Allowed, with the screens warning before it happens.</summary>
    Warn = 2,

    /// <summary>Allowed without remark.</summary>
    Allow = 3,
}

/// <summary>What happens when a sale on credit would take a customer past their limit.</summary>
public enum CreditLimitPolicy
{
    /// <summary>Not checked at all.</summary>
    Ignore = 1,

    /// <summary>Allowed, with the sales screen warning before it is saved.</summary>
    Warn = 2,

    /// <summary>Refused when the sale is posted.</summary>
    Block = 3,
}

/// <summary>The rules a firm chose that the server itself enforces.</summary>
/// <param name="NegativeStock">Whether goods may leave a position below zero.</param>
/// <param name="CreditLimit">Whether a customer's credit limit is checked, and how.</param>
/// <param name="TransfersRequireApproval">
/// Whether stock moves between branches only on a request somebody has approved.
/// </param>
/// <param name="EditsRequireApproval">
/// Whether a correction to a posted document waits for somebody to approve it.
/// </param>
/// <param name="PurchasesUpdateProductRates">
/// Whether posting a purchase sets each product's cost to what was paid for it - every
/// product, not only those costed at their last purchase rate - and rebuilds the retail
/// rate of any product that carries a profit percentage.
/// </param>
/// <remarks>
/// Kept apart from the screens' preferences, which the server stores without reading. These
/// change what a document is allowed to do, so the server validates them when they are
/// saved and reads them when a document posts; a rule the browser enforced would be a rule
/// any other client could ignore.
/// </remarks>
public sealed record FirmPolicies(
    NegativeStockPolicy NegativeStock = NegativeStockPolicy.Block,
    CreditLimitPolicy CreditLimit = CreditLimitPolicy.Warn,
    bool TransfersRequireApproval = false,
    bool EditsRequireApproval = false,
    bool PurchasesUpdateProductRates = false)
{
    /// <summary>The key the policies are stored under.</summary>
    public const string Key = "policies";

    /// <summary>The rules a firm starts with: today's behaviour, unchanged.</summary>
    public static FirmPolicies Default { get; } = new();

    /// <summary>Gets whether goods may be issued below zero.</summary>
    public bool AllowsNegativeStock => NegativeStock != NegativeStockPolicy.Block;

    /// <summary>The form the policies are stored in.</summary>
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>Reads the policies a firm chose.</summary>
public interface IFirmPolicyReader
{
    /// <summary>Reads a firm's policies, or the defaults where it has chosen none.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The policies.</returns>
    Task<FirmPolicies> GetAsync(FirmId firmId, CancellationToken cancellationToken = default);
}

/// <summary>Reads the policies from the firm's settings.</summary>
/// <remarks>
/// A block that cannot be read gives the defaults rather than an error. The defaults are
/// the stricter answers - negative stock refused - so a damaged block fails safe, and a
/// sale is never refused because a settings document could not be parsed.
/// </remarks>
public sealed class FirmPolicyReader : IFirmPolicyReader
{
    private readonly IFirmSettingRepository _settings;

    /// <summary>Initialises a new instance of the <see cref="FirmPolicyReader"/> class.</summary>
    /// <param name="settings">The settings repository.</param>
    public FirmPolicyReader(IFirmSettingRepository settings) => _settings = settings;

    /// <inheritdoc />
    public async Task<FirmPolicies> GetAsync(
        FirmId firmId,
        CancellationToken cancellationToken = default)
    {
        FirmSetting? stored = await _settings.FindAsync(firmId, FirmPolicies.Key, cancellationToken);

        if (stored is null)
        {
            return FirmPolicies.Default;
        }

        try
        {
            FirmPolicies? read = JsonSerializer.Deserialize<FirmPolicies>(
                stored.Value, FirmPolicies.Json);

            return read is not null && Enum.IsDefined(read.NegativeStock) && Enum.IsDefined(read.CreditLimit)
                ? read
                : FirmPolicies.Default;
        }
        catch (JsonException)
        {
            return FirmPolicies.Default;
        }
    }
}

/// <summary>Reads the current firm's policies.</summary>
public sealed record GetFirmPoliciesQuery : IQuery<FirmPolicies>;

/// <summary>Replaces the current firm's policies.</summary>
/// <param name="Policies">The policies, whole.</param>
public sealed record SaveFirmPoliciesCommand(FirmPolicies Policies) : ICommand;

/// <summary>Validates <see cref="SaveFirmPoliciesCommand"/>.</summary>
public sealed class SaveFirmPoliciesCommandValidator : AbstractValidator<SaveFirmPoliciesCommand>
{
    /// <summary>Initialises a new instance of the <see cref="SaveFirmPoliciesCommandValidator"/> class.</summary>
    public SaveFirmPoliciesCommandValidator()
    {
        RuleFor(c => c.Policies).NotNull();
        RuleFor(c => c.Policies.NegativeStock).IsInEnum().When(c => c.Policies is not null);
        RuleFor(c => c.Policies.CreditLimit).IsInEnum().When(c => c.Policies is not null);
    }
}

/// <summary>Handles <see cref="GetFirmPoliciesQuery"/> and <see cref="SaveFirmPoliciesCommand"/>.</summary>
public sealed class FirmPolicyHandlers
    : IQueryHandler<GetFirmPoliciesQuery, FirmPolicies>,
      ICommandHandler<SaveFirmPoliciesCommand>
{
    private readonly IFirmSettingRepository _settings;
    private readonly IFirmPolicyReader _reader;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="FirmPolicyHandlers"/> class.</summary>
    /// <param name="settings">The settings repository.</param>
    /// <param name="reader">The policy reader.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public FirmPolicyHandlers(
        IFirmSettingRepository settings,
        IFirmPolicyReader reader,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _reader = reader;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<FirmPolicies>> Handle(
        GetFirmPoliciesQuery request,
        CancellationToken cancellationToken)
    {
        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<FirmPolicies>(Error.Forbidden(
                "FirmSetting.NoFirmSelected", "A firm must be selected to read its policies."));
        }

        return Result.Success(await _reader.GetAsync(firmId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SaveFirmPoliciesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "FirmSetting.NoFirmSelected", "A firm must be selected to change its policies."));
        }

        string value = JsonSerializer.Serialize(request.Policies, FirmPolicies.Json);
        FirmSetting? existing = await _settings.FindAsync(firmId, FirmPolicies.Key, cancellationToken);

        if (existing is not null)
        {
            Result replaced = existing.Replace(value);

            if (replaced.IsFailure)
            {
                return replaced;
            }
        }
        else
        {
            Result<FirmSetting> created = FirmSetting.Create(
                _tenantContext.TenantId, firmId, FirmPolicies.Key, value);

            if (created.IsFailure)
            {
                return Result.Failure(created.Error);
            }

            _settings.Add(created.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
