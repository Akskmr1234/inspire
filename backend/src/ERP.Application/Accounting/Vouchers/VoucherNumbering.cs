using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>Takes the next number for a voucher raised by another document.</summary>
/// <remarks>
/// A sale paid at the counter raises its own receipt, and that receipt is numbered from the
/// same series as one entered by hand. A second series for receipts a sale made would be a
/// receipt book with two sets of numbers in it, which is exactly what an auditor asks
/// about.
/// </remarks>
internal static class VoucherNumbering
{
    /// <summary>Reserves the next number of a kind, creating the series if there is none.</summary>
    /// <param name="numbering">The numbering-series repository.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The firm.</param>
    /// <param name="branchId">The branch the voucher belongs to.</param>
    /// <param name="year">The financial year it falls in.</param>
    /// <param name="type">The kind of voucher.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number, or the reason one could not be issued.</returns>
    internal static async Task<Result<string>> ReserveAsync(
        INumberingSeriesRepository numbering,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear year,
        VoucherType type,
        CancellationToken cancellationToken)
    {
        string documentType = DocumentTypes.ForVoucher(type);

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
            NumberingDefaults.Apply(series, year.Code);

            numbering.Add(series);
        }

        return series.Reserve();
    }
}
