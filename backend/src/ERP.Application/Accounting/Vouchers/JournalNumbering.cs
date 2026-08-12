using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
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
    internal static async Task<Result<string>> ReserveAsync(
        INumberingSeriesRepository numbering,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        CancellationToken cancellationToken)
    {
        string documentType = DocumentTypes.ForVoucher(VoucherType.Journal);

        NumberingSeries? series = await numbering.FindForUpdateAsync(
            documentType, firmId, branchId, year.Id, cancellationToken);

        if (series is null)
        {
            Result<NumberingSeries> created = NumberingSeries.Create(
                tenantId, firmId, documentType, branchId, year.Id);

            if (created.IsFailure)
            {
                return Result.Failure<string>(created.Error);
            }

            series = created.Value;
            series.SetFormat(
                prefix: "JV", suffix: null, separator: "/", financialYearLabel: year.Code);

            numbering.Add(series);
        }

        return series.Reserve();
    }
}
