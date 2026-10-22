using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Parties;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Parties;

/// <summary>A country as a list shows it.</summary>
/// <param name="Id">The country.</param>
/// <param name="Code">Its ISO code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="CurrencyCode">The currency used there.</param>
/// <param name="CurrencyName">That currency's name.</param>
/// <param name="StateCount">How many states the firm keeps for it.</param>
/// <param name="IsActive">Whether it is offered on new records.</param>
public sealed record CountrySummary(
    Guid Id,
    string Code,
    string Name,
    string? NameArabic,
    string? CurrencyCode,
    string? CurrencyName,
    int StateCount,
    bool IsActive);

/// <summary>A state as a list shows it.</summary>
/// <param name="Id">The state.</param>
/// <param name="CountryId">The country it is in.</param>
/// <param name="CountryCode">That country's code.</param>
/// <param name="CountryName">That country's name.</param>
/// <param name="Code">Its code, which is what a party records.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="IsActive">Whether it is offered on new records.</param>
public sealed record StateSummary(
    Guid Id,
    Guid CountryId,
    string CountryCode,
    string CountryName,
    string Code,
    string Name,
    string? NameArabic,
    bool IsActive);

/// <summary>An area or a route as a list shows it.</summary>
/// <param name="Id">The area or route.</param>
/// <param name="Kind">Which of the two it is.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsActive">Whether it is offered on new records.</param>
public sealed record TerritorySummary(Guid Id, TerritoryKind Kind, string Name, bool IsActive);

/// <summary>Lists the firm's countries.</summary>
/// <param name="IncludeInactive">Whether withdrawn countries are included.</param>
public sealed record ListCountriesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CountrySummary>>;

/// <summary>Adds a country.</summary>
/// <param name="Code">Its ISO code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="CurrencyCode">The currency used there.</param>
/// <param name="CurrencyName">That currency's name.</param>
public sealed record CreateCountryCommand(
    string Code,
    string Name,
    string? NameArabic = null,
    string? CurrencyCode = null,
    string? CurrencyName = null) : ICommand<Guid>;

/// <summary>Corrects a country. Its code stays as it is: parties record it.</summary>
/// <param name="Id">The country.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="CurrencyCode">The currency used there.</param>
/// <param name="CurrencyName">That currency's name.</param>
public sealed record UpdateCountryCommand(
    Guid Id,
    string Name,
    string? NameArabic = null,
    string? CurrencyCode = null,
    string? CurrencyName = null) : ICommand;

/// <summary>Withdraws a country from use, or puts it back.</summary>
/// <param name="Id">The country.</param>
/// <param name="IsActive">Whether it should be offered.</param>
public sealed record SetCountryActiveCommand(Guid Id, bool IsActive) : ICommand;

/// <summary>Lists the firm's states, of one country or all.</summary>
/// <param name="CountryId">One country. Omit for all.</param>
/// <param name="IncludeInactive">Whether withdrawn states are included.</param>
public sealed record ListStatesQuery(Guid? CountryId = null, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<StateSummary>>;

/// <summary>Adds a state to a country.</summary>
/// <param name="CountryId">The country.</param>
/// <param name="Code">Its code, unique within the firm.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
public sealed record CreateStateCommand(
    Guid CountryId,
    string Code,
    string Name,
    string? NameArabic = null) : ICommand<Guid>;

/// <summary>Corrects a state, or moves it to another country. Its code stays as it is.</summary>
/// <param name="Id">The state.</param>
/// <param name="CountryId">The country it is in.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
public sealed record UpdateStateCommand(
    Guid Id,
    Guid CountryId,
    string Name,
    string? NameArabic = null) : ICommand;

/// <summary>Withdraws a state from use, or puts it back.</summary>
/// <param name="Id">The state.</param>
/// <param name="IsActive">Whether it should be offered.</param>
public sealed record SetStateActiveCommand(Guid Id, bool IsActive) : ICommand;

/// <summary>Lists the firm's areas and routes.</summary>
/// <param name="Kind">Areas or routes. Omit for both.</param>
/// <param name="IncludeInactive">Whether withdrawn ones are included.</param>
public sealed record ListTerritoriesQuery(TerritoryKind? Kind = null, bool IncludeInactive = false)
    : IQuery<IReadOnlyList<TerritorySummary>>;

/// <summary>Adds an area or a route.</summary>
/// <param name="Kind">Which of the two.</param>
/// <param name="Name">Its name.</param>
public sealed record CreateTerritoryCommand(TerritoryKind Kind, string Name) : ICommand<Guid>;

/// <summary>Renames an area or a route.</summary>
/// <param name="Id">The area or route.</param>
/// <param name="Name">Its name.</param>
public sealed record RenameTerritoryCommand(Guid Id, string Name) : ICommand;

/// <summary>Withdraws an area or a route from use, or puts it back.</summary>
/// <param name="Id">The area or route.</param>
/// <param name="IsActive">Whether it should be offered.</param>
public sealed record SetTerritoryActiveCommand(Guid Id, bool IsActive) : ICommand;

/// <summary>Reads and writes countries, states, areas and routes.</summary>
public interface IPlaceRepository
{
    /// <summary>Lists a firm's countries, by name.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="includeInactive">Whether withdrawn countries are included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The countries.</returns>
    Task<IReadOnlyList<Country>> ListCountriesAsync(
        FirmId firmId, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Finds a country.</summary>
    /// <param name="id">The country.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The country, or null.</returns>
    Task<Country?> FindCountryAsync(CountryId id, CancellationToken cancellationToken = default);

    /// <summary>Says whether a firm already has a country with this code.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether it does.</returns>
    Task<bool> CountryCodeExistsAsync(FirmId firmId, string code, CancellationToken cancellationToken = default);

    /// <summary>Adds a country.</summary>
    /// <param name="country">The country.</param>
    void AddCountry(Country country);

    /// <summary>Lists a firm's states, by name.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="countryId">One country, or all.</param>
    /// <param name="includeInactive">Whether withdrawn states are included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The states.</returns>
    Task<IReadOnlyList<Region>> ListRegionsAsync(
        FirmId firmId, CountryId? countryId, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Finds a state.</summary>
    /// <param name="id">The state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state, or null.</returns>
    Task<Region?> FindRegionAsync(RegionId id, CancellationToken cancellationToken = default);

    /// <summary>Says whether a firm already has a state with this code.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether it does.</returns>
    Task<bool> RegionCodeExistsAsync(FirmId firmId, string code, CancellationToken cancellationToken = default);

    /// <summary>Adds a state.</summary>
    /// <param name="region">The state.</param>
    void AddRegion(Region region);

    /// <summary>Lists a firm's areas and routes, by name.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="kind">Areas or routes, or both.</param>
    /// <param name="includeInactive">Whether withdrawn ones are included.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The areas and routes.</returns>
    Task<IReadOnlyList<Territory>> ListTerritoriesAsync(
        FirmId firmId, TerritoryKind? kind, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Finds an area or a route.</summary>
    /// <param name="id">The area or route.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>It, or null.</returns>
    Task<Territory?> FindTerritoryAsync(TerritoryId id, CancellationToken cancellationToken = default);

    /// <summary>Adds an area or a route.</summary>
    /// <param name="territory">The area or route.</param>
    void AddTerritory(Territory territory);

    /// <summary>Finds one of a party's photographs.</summary>
    /// <param name="ledgerId">The party's ledger.</param>
    /// <param name="slot">Which photograph, from one; the first is the one a card shows.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The photograph, or null.</returns>
    Task<PartyPhoto?> FindPhotoAsync(
        LedgerId ledgerId,
        int slot = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a party's photographs.</summary>
    /// <param name="ledgerId">The party's ledger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The photographs, in slot order.</returns>
    Task<IReadOnlyList<PartyPhoto>> ListPhotosAsync(
        LedgerId ledgerId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a party's photograph.</summary>
    /// <param name="photo">The photograph.</param>
    void AddPhoto(PartyPhoto photo);

    /// <summary>Removes a party's photograph.</summary>
    /// <param name="photo">The photograph.</param>
    void RemovePhoto(PartyPhoto photo);
}

/// <summary>Validates <see cref="CreateCountryCommand"/>.</summary>
public sealed class CreateCountryCommandValidator : AbstractValidator<CreateCountryCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateCountryCommandValidator"/> class.</summary>
    public CreateCountryCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Country.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Country.MaximumNameLength);
    }
}

/// <summary>Validates <see cref="CreateStateCommand"/>.</summary>
public sealed class CreateStateCommandValidator : AbstractValidator<CreateStateCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateStateCommandValidator"/> class.</summary>
    public CreateStateCommandValidator()
    {
        RuleFor(c => c.CountryId).NotEmpty();
        RuleFor(c => c.Code).NotEmpty().MaximumLength(Region.MaximumCodeLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Region.MaximumNameLength);
    }
}

/// <summary>Validates <see cref="CreateTerritoryCommand"/>.</summary>
public sealed class CreateTerritoryCommandValidator : AbstractValidator<CreateTerritoryCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateTerritoryCommandValidator"/> class.</summary>
    public CreateTerritoryCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Territory.MaximumNameLength);
    }
}

/// <summary>Handles countries, states, areas and routes.</summary>
public sealed class PlaceHandlers
    : IQueryHandler<ListCountriesQuery, IReadOnlyList<CountrySummary>>,
      ICommandHandler<CreateCountryCommand, Guid>,
      ICommandHandler<UpdateCountryCommand>,
      ICommandHandler<SetCountryActiveCommand>,
      IQueryHandler<ListStatesQuery, IReadOnlyList<StateSummary>>,
      ICommandHandler<CreateStateCommand, Guid>,
      ICommandHandler<UpdateStateCommand>,
      ICommandHandler<SetStateActiveCommand>,
      IQueryHandler<ListTerritoriesQuery, IReadOnlyList<TerritorySummary>>,
      ICommandHandler<CreateTerritoryCommand, Guid>,
      ICommandHandler<RenameTerritoryCommand>,
      ICommandHandler<SetTerritoryActiveCommand>
{
    private readonly IPlaceRepository _places;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="PlaceHandlers"/> class.</summary>
    /// <param name="places">The place repository.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PlaceHandlers(IPlaceRepository places, ITenantContext tenantContext, IUnitOfWork unitOfWork)
    {
        _places = places;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<CountrySummary>>> Handle(
        ListCountriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Firm() is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<CountrySummary>>(NoFirm());
        }

        IReadOnlyList<Country> countries = await _places.ListCountriesAsync(
            firmId, request.IncludeInactive, cancellationToken);
        IReadOnlyList<Region> regions = await _places.ListRegionsAsync(
            firmId, null, includeInactive: false, cancellationToken);

        Dictionary<CountryId, int> counts = regions
            .GroupBy(region => region.CountryId)
            .ToDictionary(group => group.Key, group => group.Count());

        return Result.Success<IReadOnlyList<CountrySummary>>(
        [
            .. countries.Select(country => new CountrySummary(
                country.Id.Value, country.Code, country.Name, country.NameArabic,
                country.CurrencyCode, country.CurrencyName,
                counts.GetValueOrDefault(country.Id), country.IsActive)),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateCountryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Firm() is not { } firmId)
        {
            return Result.Failure<Guid>(NoFirm());
        }

        if (await _places.CountryCodeExistsAsync(firmId, request.Code.Trim(), cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Country.CodeInUse", $"'{request.Code.Trim().ToUpperInvariant()}' is already a country."));
        }

        Result<Country> created = Country.Create(
            _tenantContext.TenantId, firmId, request.Code, request.Name,
            request.CurrencyCode, request.CurrencyName);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Result named = created.Value.Change(
            request.Name, request.NameArabic, request.CurrencyCode, request.CurrencyName);

        if (named.IsFailure)
        {
            return Result.Failure<Guid>(named.Error);
        }

        _places.AddCountry(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(UpdateCountryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Country> found = await FindCountryAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result changed = found.Value.Change(
            request.Name, request.NameArabic, request.CurrencyCode, request.CurrencyName);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetCountryActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Country> found = await FindCountryAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        if (request.IsActive)
        {
            found.Value.Activate();
        }
        else
        {
            found.Value.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<StateSummary>>> Handle(
        ListStatesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Firm() is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<StateSummary>>(NoFirm());
        }

        IReadOnlyList<Region> regions = await _places.ListRegionsAsync(
            firmId,
            request.CountryId is { } country ? CountryId.From(country) : null,
            request.IncludeInactive,
            cancellationToken);

        Dictionary<CountryId, Country> countries = (await _places.ListCountriesAsync(
                firmId, includeInactive: true, cancellationToken))
            .ToDictionary(country => country.Id);

        return Result.Success<IReadOnlyList<StateSummary>>(
        [
            .. regions.Select(region =>
            {
                Country? country = countries.GetValueOrDefault(region.CountryId);

                return new StateSummary(
                    region.Id.Value, region.CountryId.Value, country?.Code ?? string.Empty,
                    country?.Name ?? string.Empty, region.Code, region.Name, region.NameArabic,
                    region.IsActive);
            }),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateStateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Country> country = await FindCountryAsync(request.CountryId, cancellationToken);

        if (country.IsFailure)
        {
            return Result.Failure<Guid>(country.Error);
        }

        if (await _places.RegionCodeExistsAsync(country.Value.FirmId, request.Code.Trim(), cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Region.CodeInUse",
                $"'{request.Code.Trim().ToUpperInvariant()}' is already the code of a state."));
        }

        Result<Region> created = Region.Create(country.Value, request.Code, request.Name, request.NameArabic);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        _places.AddRegion(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(UpdateStateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Region> found = await FindRegionAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result<Country> country = await FindCountryAsync(request.CountryId, cancellationToken);

        if (country.IsFailure)
        {
            return country;
        }

        Result changed = found.Value.Change(country.Value, request.Name, request.NameArabic);

        if (changed.IsFailure)
        {
            return changed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetStateActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Region> found = await FindRegionAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        if (request.IsActive)
        {
            found.Value.Activate();
        }
        else
        {
            found.Value.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TerritorySummary>>> Handle(
        ListTerritoriesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Firm() is not { } firmId)
        {
            return Result.Failure<IReadOnlyList<TerritorySummary>>(NoFirm());
        }

        IReadOnlyList<Territory> territories = await _places.ListTerritoriesAsync(
            firmId, request.Kind, request.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<TerritorySummary>>(
        [
            .. territories.Select(territory => new TerritorySummary(
                territory.Id.Value, territory.Kind, territory.Name, territory.IsActive)),
        ]);
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(CreateTerritoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Firm() is not { } firmId)
        {
            return Result.Failure<Guid>(NoFirm());
        }

        IReadOnlyList<Territory> existing = await _places.ListTerritoriesAsync(
            firmId, request.Kind, includeInactive: true, cancellationToken);

        if (existing.Any(territory =>
            string.Equals(territory.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "Territory.NameInUse", $"There is already a {request.Kind} called '{request.Name.Trim()}'."));
        }

        Result<Territory> created = Territory.Create(
            _tenantContext.TenantId, firmId, request.Kind, request.Name);

        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        _places.AddTerritory(created.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(created.Value.Id.Value);
    }

    /// <inheritdoc />
    public async Task<Result> Handle(RenameTerritoryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Territory> found = await FindTerritoryAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        Result renamed = found.Value.Rename(request.Name);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetTerritoryActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Territory> found = await FindTerritoryAsync(request.Id, cancellationToken);

        if (found.IsFailure)
        {
            return found;
        }

        if (request.IsActive)
        {
            found.Value.Activate();
        }
        else
        {
            found.Value.Deactivate();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static Error NoFirm() =>
        Error.Forbidden("Places.NoFirmSelected", "A firm must be selected first.");

    private FirmId? Firm() => _tenantContext.FirmId;

    private async Task<Result<Country>> FindCountryAsync(Guid id, CancellationToken cancellationToken)
    {
        Country? country = await _places.FindCountryAsync(CountryId.From(id), cancellationToken);

        return country is null || country.FirmId != Firm()
            ? Result.Failure<Country>(Error.NotFound("Country.NotFound", "No such country in the selected firm."))
            : Result.Success(country);
    }

    private async Task<Result<Region>> FindRegionAsync(Guid id, CancellationToken cancellationToken)
    {
        Region? region = await _places.FindRegionAsync(RegionId.From(id), cancellationToken);

        return region is null || region.FirmId != Firm()
            ? Result.Failure<Region>(Error.NotFound("Region.NotFound", "No such state in the selected firm."))
            : Result.Success(region);
    }

    private async Task<Result<Territory>> FindTerritoryAsync(Guid id, CancellationToken cancellationToken)
    {
        Territory? territory = await _places.FindTerritoryAsync(TerritoryId.From(id), cancellationToken);

        return territory is null || territory.FirmId != Firm()
            ? Result.Failure<Territory>(Error.NotFound(
                "Territory.NotFound", "No such area or route in the selected firm."))
            : Result.Success(territory);
    }
}
