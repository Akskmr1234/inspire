using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>Takes the next number for a journal raised by another document.</summary>
/// <remarks>
/// Shared because two kinds of document now raise journals of their own - a stock
/// movement and a sale - and both draw from the same series. Two copies of this would be
/// two places for a firm's journal numbering to diverge, and a gap in a voucher sequence
/// is exactly what an auditor asks about.
/// </remarks>
internal static class JournalNumbering
{
    /// <summary>Reserves the next journal number, creating the series if there is none.</summary>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch the journal belongs to.</param>
    /// <param name="year">The financial year it falls in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number, or the reason one could not be issued.</returns>
    internal static Task<Result<string>> ReserveAsync(
        INumberingSeriesRepository numbering,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken) =>
        VoucherNumbering.ReserveAsync(
            numbering, tenantId, firmId, branchId, year, VoucherType.Journal, cancellationToken);
}
