using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;

namespace ERP.Application.Accounting.Vouchers;

/// <summary>Fetches a voucher and its lines.</summary>
/// <param name="VoucherId">The voucher identifier.</param>
public sealed record GetVoucherByIdQuery(Guid VoucherId) : IQuery<VoucherDetailResponse>;

/// <summary>One line of a voucher, as returned to a client.</summary>
/// <param name="LineNumber">The line's position, from one.</param>
/// <param name="LedgerId">The ledger posted against.</param>
/// <param name="Side">Whether the line debits or credits.</param>
/// <param name="DebitAmount">The amount when this is a debit line, otherwise zero.</param>
/// <param name="CreditAmount">The amount when this is a credit line, otherwise zero.</param>
/// <param name="BaseAmount">The amount in the firm's base currency.</param>
/// <param name="Narration">The line narration.</param>
/// <remarks>
/// Debit and credit are presented as separate fields because that is the shape of
/// the entry grid and the printed voucher, where one column is always blank.
/// </remarks>
public sealed record VoucherLineResponse(
    int LineNumber,
    Guid LedgerId,
    EntrySide Side,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal BaseAmount,
    string? Narration);

/// <summary>A voucher, as returned to a client.</summary>
/// <param name="VoucherId">The voucher identifier.</param>
/// <param name="Number">The document number.</param>
/// <param name="Type">The kind of voucher.</param>
/// <param name="Status">Where it stands in its lifecycle.</param>
/// <param name="Date">The document date.</param>
/// <param name="Currency">The entry currency.</param>
/// <param name="BaseCurrency">The firm's base currency.</param>
/// <param name="ExchangeRate">The rate applied.</param>
/// <param name="TotalDebit">Total debits, in the entry currency.</param>
/// <param name="TotalCredit">Total credits, in the entry currency.</param>
/// <param name="ReferenceNumber">The related reference.</param>
/// <param name="Narration">The voucher narration.</param>
/// <param name="PaymentMode">The payment mode.</param>
/// <param name="Lines">The postings.</param>
public sealed record VoucherDetailResponse(
    Guid VoucherId,
    string Number,
    VoucherType Type,
    VoucherStatus Status,
    DateOnly Date,
    string Currency,
    string BaseCurrency,
    decimal ExchangeRate,
    decimal TotalDebit,
    decimal TotalCredit,
    string? ReferenceNumber,
    string? Narration,
    string? PaymentMode,
    IReadOnlyList<VoucherLineResponse> Lines);

/// <summary>Handles <see cref="GetVoucherByIdQuery"/>.</summary>
public sealed class GetVoucherByIdQueryHandler
    : IQueryHandler<GetVoucherByIdQuery, VoucherDetailResponse>
{
    private readonly IVoucherRepository _vouchers;

    /// <summary>Initialises a new instance of the <see cref="GetVoucherByIdQueryHandler"/> class.</summary>
    /// <param name="vouchers">The voucher repository.</param>
    public GetVoucherByIdQueryHandler(IVoucherRepository vouchers) => _vouchers = vouchers;

    /// <inheritdoc />
    /// <remarks>
    /// No tenant check appears here, and none is needed: the global query filter and
    /// the row-level-security policy both apply, so a voucher belonging to another
    /// tenant simply is not found. That is why the handler can be this short without
    /// being unsafe.
    /// </remarks>
    public async Task<Result<VoucherDetailResponse>> Handle(
        GetVoucherByIdQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Voucher? voucher = await _vouchers.FindAsync(
            VoucherId.From(request.VoucherId), cancellationToken);

        if (voucher is null)
        {
            return Result.Failure<VoucherDetailResponse>(Error.NotFound(
                "Voucher.NotFound", "No such voucher."));
        }

        return Result.Success(voucher.ToDetailResponse());
    }
}

/// <summary>
/// Hand-written mapping from the domain onto response contracts.
/// </summary>
/// <remarks>
/// Explicit rather than AutoMapper-based. Renaming or removing a domain property
/// becomes a build error here, instead of a silently-null field on an invoice - a
/// materially better failure mode for financial documents. See
/// <c>docs/adr/0002-third-party-licensing.md</c>.
/// </remarks>
internal static class VoucherMappings
{
    internal static VoucherDetailResponse ToDetailResponse(this Voucher voucher) => new(
        voucher.Id.Value,
        voucher.Number,
        voucher.Type,
        voucher.Status,
        voucher.Date,
        voucher.Currency.Code,
        voucher.BaseCurrency.Code,
        voucher.ExchangeRate,
        voucher.TotalDebit.Amount,
        voucher.TotalCredit.Amount,
        voucher.ReferenceNumber,
        voucher.Narration,
        voucher.PaymentMode,
        [.. voucher.Lines
            .OrderBy(l => l.LineNumber)
            .Select(l => new VoucherLineResponse(
                l.LineNumber,
                l.LedgerId.Value,
                l.Side,
                l.DebitAmount.Amount,
                l.CreditAmount.Amount,
                l.BaseAmount.Amount,
                l.Narration))]);
}
