using ERP.SharedKernel.Results;

namespace ERP.Application.Platform.Branches;

/// <summary>A branch as the settings screen and the branch switcher list it.</summary>
/// <param name="BranchId">The branch.</param>
/// <param name="Code">Its code, fixed once made: documents are numbered by it.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic, if it has one.</param>
/// <param name="IsHeadOffice">Whether it is the firm's head office.</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="Address">Its address.</param>
/// <param name="Phone">Its phone number.</param>
/// <param name="Email">Its email address.</param>
/// <param name="UserCount">How many users may work in it.</param>
public sealed record BranchSummary(
    Guid BranchId,
    string Code,
    string Name,
    string? NameArabic,
    bool IsHeadOffice,
    bool IsActive,
    string? Address,
    string? Phone,
    string? Email,
    int UserCount);

/// <summary>A branch as somebody enters or corrects it.</summary>
/// <param name="Code">Its code. Read when it is made; a branch keeps its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="NameArabic">Its name in Arabic.</param>
/// <param name="Address">Its address.</param>
/// <param name="Phone">Its phone number.</param>
/// <param name="Email">Its email address.</param>
/// <param name="IsActive">Whether it is in use.</param>
/// <param name="FirmId">
/// The firm it belongs to, read when it is made; <see langword="null"/> for the firm in
/// session. A branch stays with its firm.
/// </param>
public sealed record SaveBranchRequest(
    string Code,
    string Name,
    string? NameArabic,
    string? Address,
    string? Phone,
    string? Email,
    bool IsActive = true,
    Guid? FirmId = null);

/// <summary>The branches of the firm in session: listing, adding and correcting them.</summary>
public interface IBranchAdministration
{
    /// <summary>Lists every branch of the firm, the inactive ones too.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branches, head office first.</returns>
    Task<IReadOnlyList<BranchSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the active branches the signed-in user may work in.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branches, head office first.</returns>
    Task<IReadOnlyList<BranchSummary>> ListMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a branch to the firm, and lets its maker work in it.</summary>
    /// <param name="request">The branch.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branch, or why it could not be added.</returns>
    Task<Result<BranchSummary>> CreateAsync(
        SaveBranchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Corrects a branch: its name, contact details and whether it is in use.</summary>
    /// <param name="branchId">The branch.</param>
    /// <param name="request">The branch as it should be. The code is ignored.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The branch as it now stands.</returns>
    Task<Result<BranchSummary>> UpdateAsync(
        Guid branchId,
        SaveBranchRequest request,
        CancellationToken cancellationToken = default);
}
