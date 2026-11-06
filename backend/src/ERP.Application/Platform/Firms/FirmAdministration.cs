using ERP.Application.Platform.Branches;
using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Firms;

/// <summary>A firm, with its branches, as the settings screen and the firm switcher list it.</summary>
/// <param name="FirmId">The firm.</param>
/// <param name="Code">Its code, fixed once made.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic, if it has one.</param>
/// <param name="BaseCurrency">The currency its books are kept in, fixed once made.</param>
/// <param name="TaxRegistrationNumber">Its tax registration number.</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="UserCount">How many users may work in it.</param>
/// <param name="Branches">Its branches, head office first.</param>
public sealed record FirmSummary(
    Guid FirmId,
    string Code,
    string Name,
    string? NameArabic,
    string BaseCurrency,
    string? TaxRegistrationNumber,
    bool IsActive,
    int UserCount,
    IReadOnlyList<BranchSummary> Branches);

/// <summary>A firm as somebody enters or corrects it.</summary>
/// <param name="Code">Its code. Read when it is made; a firm keeps its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="BaseCurrency">
/// The currency its books are kept in, as an ISO code. Read when it is made; a firm keeps
/// its currency, since every figure in its books is in it.
/// </param>
/// <param name="TaxRegistrationNumber">Its tax registration number.</param>
/// <param name="IsActive">Whether it is in use.</param>
public sealed record SaveFirmRequest(
    string Code,
    string Name,
    string? NameArabic,
    string BaseCurrency,
    string? TaxRegistrationNumber,
    bool IsActive = true);

/// <summary>The tenant's firms: listing, adding and correcting them.</summary>
public interface IFirmAdministration
{
    /// <summary>Lists every firm with every branch, the inactive ones too.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firms, by code.</returns>
    Task<IReadOnlyList<FirmSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the active firms the signed-in user may work in, each with only the active
    /// branches they may work in.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firms, by code.</returns>
    Task<IReadOnlyList<FirmSummary>> ListMineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a firm, with a head office, set up as the first firm was - its financial
    /// year, chart of accounts, menu, dashboards and tax slabs - and lets its maker work in it.
    /// </summary>
    /// <param name="request">The firm.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firm, or why it could not be added.</returns>
    Task<Result<FirmSummary>> CreateAsync(
        SaveFirmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects a firm: its name, Arabic name, tax number and whether it is in use.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="request">The firm as it should be. The code and currency are ignored.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The firm as it now stands.</returns>
    Task<Result<FirmSummary>> UpdateAsync(
        Guid firmId,
        SaveFirmRequest request,
        CancellationToken cancellationToken = default);
}
