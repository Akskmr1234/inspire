using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Platform;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Platform.Settings;

/// <summary>Reads one named block of the current firm's configuration.</summary>
/// <param name="Key">The block's name, such as <c>preferences</c>.</param>
public sealed record GetFirmSettingQuery(string Key) : IQuery<FirmSettingResponse>;

/// <summary>A block of configuration, or the absence of one.</summary>
/// <param name="Key">The block's name.</param>
/// <param name="Value">The document, or <see langword="null"/> where none was ever saved.</param>
/// <param name="ModifiedAtUtc">When it last changed, where it has.</param>
public sealed record FirmSettingResponse(string Key, string? Value, DateTimeOffset? ModifiedAtUtc);

/// <summary>Saves one named block of the current firm's configuration, replacing it whole.</summary>
/// <param name="Key">The block's name.</param>
/// <param name="Value">The document.</param>
public sealed record SaveFirmSettingCommand(string Key, string Value) : ICommand;

/// <summary>Validates <see cref="SaveFirmSettingCommand"/>.</summary>
public sealed class SaveFirmSettingCommandValidator : AbstractValidator<SaveFirmSettingCommand>
{
    /// <summary>Initialises a new instance of the <see cref="SaveFirmSettingCommandValidator"/> class.</summary>
    public SaveFirmSettingCommandValidator()
    {
        RuleFor(c => c.Key).NotEmpty().MaximumLength(FirmSetting.MaximumKeyLength);
        RuleFor(c => c.Value).NotEmpty().MaximumLength(FirmSetting.MaximumValueLength);
    }
}

/// <summary>Reads and writes firm settings.</summary>
public interface IFirmSettingRepository
{
    /// <summary>Finds a firm's block by name.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="key">The normalised name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The block, or <see langword="null"/>.</returns>
    Task<FirmSetting?> FindAsync(
        FirmId firmId,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a new block.</summary>
    /// <param name="setting">The block.</param>
    void Add(FirmSetting setting);
}

/// <summary>Handles <see cref="GetFirmSettingQuery"/>.</summary>
public sealed class GetFirmSettingQueryHandler
    : IQueryHandler<GetFirmSettingQuery, FirmSettingResponse>
{
    private readonly IFirmSettingRepository _settings;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetFirmSettingQueryHandler"/> class.</summary>
    /// <param name="settings">The settings repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    public GetFirmSettingQueryHandler(
        IFirmSettingRepository settings,
        ITenantContext tenantContext)
    {
        _settings = settings;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<FirmSettingResponse>> Handle(
        GetFirmSettingQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result keyCheck = FirmSetting.ValidateKey(request.Key);

        if (keyCheck.IsFailure)
        {
            return Result.Failure<FirmSettingResponse>(keyCheck.Error);
        }

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure<FirmSettingResponse>(Error.Forbidden(
                "FirmSetting.NoFirmSelected", "A firm must be selected to read its settings."));
        }

        string key = FirmSetting.Normalise(request.Key);
        FirmSetting? setting = await _settings.FindAsync(firmId, key, cancellationToken);

        // Never having saved one is the ordinary case for a new firm, not an error: the
        // client falls back to its own defaults.
        return Result.Success(new FirmSettingResponse(
            key, setting?.Value, setting?.ModifiedAtUtc ?? setting?.CreatedAtUtc));
    }
}

/// <summary>Handles <see cref="SaveFirmSettingCommand"/>.</summary>
public sealed class SaveFirmSettingCommandHandler : ICommandHandler<SaveFirmSettingCommand>
{
    private readonly IFirmSettingRepository _settings;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="SaveFirmSettingCommandHandler"/> class.</summary>
    /// <param name="settings">The settings repository.</param>
    /// <param name="tenantContext">The ambient tenant.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public SaveFirmSettingCommandHandler(
        IFirmSettingRepository settings,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> Handle(
        SaveFirmSettingCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result keyCheck = FirmSetting.ValidateKey(request.Key);

        if (keyCheck.IsFailure)
        {
            return keyCheck;
        }

        if (_tenantContext.FirmId is not { } firmId)
        {
            return Result.Failure(Error.Forbidden(
                "FirmSetting.NoFirmSelected", "A firm must be selected to change its settings."));
        }

        string key = FirmSetting.Normalise(request.Key);
        FirmSetting? existing = await _settings.FindAsync(firmId, key, cancellationToken);

        // An upsert: the screen is saving "the firm's preferences", and making it know
        // whether anybody had saved them before would be asking it to track something
        // it has no reason to care about.
        if (existing is not null)
        {
            Result replaced = existing.Replace(request.Value);

            if (replaced.IsFailure)
            {
                return replaced;
            }
        }
        else
        {
            Result<FirmSetting> created = FirmSetting.Create(
                _tenantContext.TenantId, firmId, key, request.Value);

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
