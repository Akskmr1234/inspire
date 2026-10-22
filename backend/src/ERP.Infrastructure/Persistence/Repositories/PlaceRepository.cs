using ERP.Application.Parties;
using ERP.Domain.Accounting;
using ERP.Domain.Parties;
using ERP.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>The EF Core repository for countries, states, areas, routes and party photographs.</summary>
public sealed class PlaceRepository : IPlaceRepository
{
    private readonly ErpDbContext _context;

    /// <summary>Initialises a new instance of the <see cref="PlaceRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public PlaceRepository(ErpDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Country>> ListCountriesAsync(
        FirmId firmId,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await _context.Countries
            .Where(country => country.FirmId == firmId && (includeInactive || country.IsActive))
            .OrderBy(country => country.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Country?> FindCountryAsync(CountryId id, CancellationToken cancellationToken = default) =>
        _context.Countries.FirstOrDefaultAsync(country => country.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CountryCodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalised = code.Trim().ToUpperInvariant();

        return _context.Countries.AnyAsync(
            country => country.FirmId == firmId && country.Code == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public void AddCountry(Country country) => _context.Countries.Add(country);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Region>> ListRegionsAsync(
        FirmId firmId,
        CountryId? countryId,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await _context.Regions
            .Where(region => region.FirmId == firmId
                && (countryId == null || region.CountryId == countryId)
                && (includeInactive || region.IsActive))
            .OrderBy(region => region.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Region?> FindRegionAsync(RegionId id, CancellationToken cancellationToken = default) =>
        _context.Regions.FirstOrDefaultAsync(region => region.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RegionCodeExistsAsync(
        FirmId firmId,
        string code,
        CancellationToken cancellationToken = default)
    {
        string normalised = code.Trim().ToUpperInvariant();

        return _context.Regions.AnyAsync(
            region => region.FirmId == firmId && region.Code == normalised, cancellationToken);
    }

    /// <inheritdoc />
    public void AddRegion(Region region) => _context.Regions.Add(region);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Territory>> ListTerritoriesAsync(
        FirmId firmId,
        TerritoryKind? kind,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await _context.Territories
            .Where(territory => territory.FirmId == firmId
                && (kind == null || territory.Kind == kind)
                && (includeInactive || territory.IsActive))
            .OrderBy(territory => territory.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Territory?> FindTerritoryAsync(TerritoryId id, CancellationToken cancellationToken = default) =>
        _context.Territories.FirstOrDefaultAsync(territory => territory.Id == id, cancellationToken);

    /// <inheritdoc />
    public void AddTerritory(Territory territory) => _context.Territories.Add(territory);

    /// <inheritdoc />
    public Task<PartyPhoto?> FindPhotoAsync(
        LedgerId ledgerId,
        int slot = 1,
        CancellationToken cancellationToken = default) =>
        _context.PartyPhotos.FirstOrDefaultAsync(
            photo => photo.LedgerId == ledgerId && photo.Slot == slot, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PartyPhoto>> ListPhotosAsync(
        LedgerId ledgerId,
        CancellationToken cancellationToken = default) =>
        await _context.PartyPhotos
            .Where(photo => photo.LedgerId == ledgerId)
            .OrderBy(photo => photo.Slot)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void AddPhoto(PartyPhoto photo) => _context.PartyPhotos.Add(photo);

    /// <inheritdoc />
    public void RemovePhoto(PartyPhoto photo) => _context.PartyPhotos.Remove(photo);
}
