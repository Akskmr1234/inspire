using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Results;
using FluentValidation;

namespace ERP.Application.Accounting.Reports;

/// <summary>The output tax charged over a period, document by document.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
/// <remarks>
/// §7.3's Output Tax report. Read from the sales documents rather than from the tax
/// ledgers, because a return has to state supplies <em>by rate</em> and a ledger posting
/// carries only the money - the rate and the taxable value it was charged on live on the
/// document line.
/// </remarks>
public sealed record GetOutputTaxQuery(DateOnly From, DateOnly To)
    : IQuery<OutputTaxReport>;

/// <summary>The input tax incurred over a period, posting by posting.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
/// <remarks>
/// §7.3's Input Tax report. Read from postings to the input accounts the firm's tax
/// account map names, which is the business's answer of 2026-08-12: it is right today for
/// the journals somebody writes by hand, and it goes on being right when purchase arrives,
/// because purchase postings land in the same accounts.
/// </remarks>
public sealed record GetInputTaxQuery(DateOnly From, DateOnly To) : IQuery<InputTaxReport>;

/// <summary>What a firm owes the state for a period, head by head.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
public sealed record GetTaxSummaryQuery(DateOnly From, DateOnly To) : IQuery<TaxSummaryReport>;

/// <summary>One document's charge under one head.</summary>
/// <param name="DocumentId">The sales document.</param>
/// <param name="Number">Its number.</param>
/// <param name="Kind">Whether it sold goods or took them back.</param>
/// <param name="Date">Its date.</param>
/// <param name="CustomerCode">The customer's account code.</param>
/// <param name="CustomerName">Their name.</param>
/// <param name="TaxRegistrationNumber">Their VAT number or GSTIN, where recorded.</param>
/// <param name="StateCode">Their state, which decided IGST against CGST plus SGST.</param>
/// <param name="Component">The head charged.</param>
/// <param name="Percentage">The rate it was charged at.</param>
/// <param name="TaxableAmount">
/// The value the head was charged on. Stated against each head, so a supply carrying both
/// CGST and SGST reports its base twice here - the totals below count it once.
/// </param>
/// <param name="TaxAmount">What the head came to. Negative on a return.</param>
public sealed record OutputTaxRow(
    Guid DocumentId,
    string Number,
    SalesDocumentKind Kind,
    DateOnly Date,
    string CustomerCode,
    string CustomerName,
    string? TaxRegistrationNumber,
    string? StateCode,
    TaxComponentType Component,
    decimal Percentage,
    decimal TaxableAmount,
    decimal TaxAmount);

/// <summary>What one head came to over the period.</summary>
/// <param name="Component">The head.</param>
/// <param name="TaxAmount">The tax.</param>
public sealed record TaxHeadTotal(TaxComponentType Component, decimal TaxAmount);

/// <summary>The output tax of a period.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
/// <param name="Regime">The firm's statutory tax system.</param>
/// <param name="Currency">The currency the figures are in.</param>
/// <param name="TaxableSupplies">
/// What was supplied, net of returns and counted once however many heads it carried.
/// </param>
/// <param name="ZeroRatedSupplies">Supplies that carried no tax at all.</param>
/// <param name="Totals">The tax by head.</param>
/// <param name="Rows">The documents behind those totals.</param>
public sealed record OutputTaxReport(
    DateOnly From,
    DateOnly To,
    TaxRegime Regime,
    string Currency,
    decimal TaxableSupplies,
    decimal ZeroRatedSupplies,
    IReadOnlyList<TaxHeadTotal> Totals,
    IReadOnlyList<OutputTaxRow> Rows);

/// <summary>One head of input tax, from a purchase or from a hand-written journal.</summary>
/// <param name="DocumentId">The purchase document, or the voucher where there is none.</param>
/// <param name="Number">The firm's own number for it.</param>
/// <param name="Kind">
/// Whether goods were bought or sent back. Absent where no purchase document produced the
/// row - a journal somebody wrote straight into a tax account has no direction of its own.
/// </param>
/// <param name="Date">Its date.</param>
/// <param name="SupplierCode">The supplier's account code, where one is known.</param>
/// <param name="SupplierName">Their name.</param>
/// <param name="TaxRegistrationNumber">Their VAT number or GSTIN, where recorded.</param>
/// <param name="SupplierInvoiceNumber">
/// The number on their own tax invoice, which is what a reclaim is made against and what
/// both regimes' returns report the line under.
/// </param>
/// <param name="Component">The head charged.</param>
/// <param name="Percentage">The rate it was charged at. Nought on a hand-written entry.</param>
/// <param name="TaxableAmount">
/// The value the head was charged on, stated against each head so a supply carrying both
/// CGST and SGST reports its base twice here - the totals count it once. Absent where
/// nothing knows it, which is every row a journal produced.
/// </param>
/// <param name="TaxAmount">
/// What was recovered. Negative on a return, and on a hand-written credit.
/// </param>
/// <param name="Narration">Whatever the journal line said, where it was one.</param>
public sealed record InputTaxRow(
    Guid DocumentId,
    string Number,
    PurchaseDocumentKind? Kind,
    DateOnly Date,
    string SupplierCode,
    string SupplierName,
    string? TaxRegistrationNumber,
    string? SupplierInvoiceNumber,
    TaxComponentType Component,
    decimal Percentage,
    decimal? TaxableAmount,
    decimal TaxAmount,
    string? Narration);

/// <summary>The input tax of a period.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
/// <param name="Regime">The firm's statutory tax system.</param>
/// <param name="Currency">The currency the figures are in.</param>
/// <param name="TaxablePurchases">
/// What was bought, net of returns and counted once however many heads it carried. Only
/// what a purchase document accounts for: a journal somebody wrote into a tax account
/// contributes tax without a base, and adding a guess for it would be a figure on a
/// statutory return that no document supports.
/// </param>
/// <param name="ZeroRatedPurchases">Purchases that carried no tax at all.</param>
/// <param name="Totals">The tax by head.</param>
/// <param name="Rows">The documents and postings behind those totals.</param>
/// <remarks>
/// Built from purchase documents, the way the output side is built from sales ones, plus
/// any posting to an input account that no purchase document produced. The second half
/// matters: input tax booked by hand is still input tax, and dropping it from the listing
/// while it sits in the ledger would make the return understate what is reclaimable.
/// </remarks>
public sealed record InputTaxReport(
    DateOnly From,
    DateOnly To,
    TaxRegime Regime,
    string Currency,
    decimal TaxablePurchases,
    decimal ZeroRatedPurchases,
    IReadOnlyList<TaxHeadTotal> Totals,
    IReadOnlyList<InputTaxRow> Rows);

/// <summary>One head's position for the period.</summary>
/// <param name="Component">The head.</param>
/// <param name="OutputTax">What was charged on supplies, from the documents.</param>
/// <param name="InputTax">What was incurred, from the postings.</param>
/// <param name="NetPayable">The difference. Negative means the state owes the firm.</param>
/// <param name="OutputTaxPosted">
/// The same head's movement on its own output ledger over the period.
/// </param>
/// <param name="Difference">
/// What the ledger says less what the documents say. Anything other than zero means
/// output tax reached the books by some route other than a sales document - a journal
/// somebody wrote by hand - and a return built from the documents alone would understate
/// it. Surfaced rather than reconciled silently, because only a person can say which of
/// the two is right.
/// </param>
/// <param name="InputTaxPosted">The same, on the input ledger.</param>
/// <param name="InputDifference">
/// The input side's version of the same check, and it should always be nought: the input
/// listing counts hand-written postings as well as purchases, so anything left over is
/// tax on a ledger the return cannot see at all - an account mapped to a head after the
/// postings were made, most likely.
/// </param>
public sealed record TaxSummaryLine(
    TaxComponentType Component,
    decimal OutputTax,
    decimal InputTax,
    decimal NetPayable,
    decimal OutputTaxPosted,
    decimal Difference,
    decimal InputTaxPosted,
    decimal InputDifference);

/// <summary>What a firm owes the state for a period.</summary>
/// <param name="From">The first day counted.</param>
/// <param name="To">The last.</param>
/// <param name="Regime">The firm's statutory tax system.</param>
/// <param name="Currency">The currency the figures are in.</param>
/// <param name="TaxableSupplies">What was supplied, net of returns.</param>
/// <param name="ZeroRatedSupplies">Supplies that carried no tax.</param>
/// <param name="TaxablePurchases">What was bought, net of returns.</param>
/// <param name="ZeroRatedPurchases">Purchases that carried no tax.</param>
/// <param name="Lines">One line per head the firm's regime uses.</param>
/// <param name="NetPayable">The total owed, or reclaimable where negative.</param>
/// <param name="IsReconciled">
/// Whether every head's ledger agrees with its documents. False is not an error; it means
/// somebody should look before filing.
/// </param>
public sealed record TaxSummaryReport(
    DateOnly From,
    DateOnly To,
    TaxRegime Regime,
    string Currency,
    decimal TaxableSupplies,
    decimal ZeroRatedSupplies,
    decimal TaxablePurchases,
    decimal ZeroRatedPurchases,
    IReadOnlyList<TaxSummaryLine> Lines,
    decimal NetPayable,
    bool IsReconciled);

/// <summary>Validates a tax report's period.</summary>
/// <typeparam name="TQuery">The query being validated.</typeparam>
internal sealed class TaxPeriodValidator<TQuery> : AbstractValidator<TQuery>
{
    /// <summary>Initialises a new instance of the <see cref="TaxPeriodValidator{TQuery}"/> class.</summary>
    /// <param name="from">Reads the start of the period.</param>
    /// <param name="to">Reads its end.</param>
    internal TaxPeriodValidator(Func<TQuery, DateOnly> from, Func<TQuery, DateOnly> to)
    {
        RuleFor(query => to(query))
            .GreaterThanOrEqualTo(query => from(query))
            .WithMessage("A tax period cannot end before it starts.");

        RuleFor(query => from(query))
            .NotEqual(default(DateOnly))
            .WithMessage("A tax period must state the day it starts.");
    }
}

/// <summary>Validates <see cref="GetOutputTaxQuery"/>.</summary>
public sealed class GetOutputTaxQueryValidator : AbstractValidator<GetOutputTaxQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetOutputTaxQueryValidator"/> class.</summary>
    public GetOutputTaxQueryValidator() =>
        Include(new TaxPeriodValidator<GetOutputTaxQuery>(q => q.From, q => q.To));
}

/// <summary>Validates <see cref="GetInputTaxQuery"/>.</summary>
public sealed class GetInputTaxQueryValidator : AbstractValidator<GetInputTaxQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetInputTaxQueryValidator"/> class.</summary>
    public GetInputTaxQueryValidator() =>
        Include(new TaxPeriodValidator<GetInputTaxQuery>(q => q.From, q => q.To));
}

/// <summary>Validates <see cref="GetTaxSummaryQuery"/>.</summary>
public sealed class GetTaxSummaryQueryValidator : AbstractValidator<GetTaxSummaryQuery>
{
    /// <summary>Initialises a new instance of the <see cref="GetTaxSummaryQueryValidator"/> class.</summary>
    public GetTaxSummaryQueryValidator() =>
        Include(new TaxPeriodValidator<GetTaxSummaryQuery>(q => q.From, q => q.To));
}

/// <summary>Handles <see cref="GetOutputTaxQuery"/>.</summary>
public sealed class GetOutputTaxQueryHandler : IQueryHandler<GetOutputTaxQuery, OutputTaxReport>
{
    private readonly ITaxReturnReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetOutputTaxQueryHandler"/> class.</summary>
    /// <param name="reader">The tax return reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetOutputTaxQueryHandler(ITaxReturnReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<OutputTaxReport>> Handle(
        GetOutputTaxQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is { } firmId
            ? Result.Success(
                await _reader.ReadOutputAsync(firmId, request.From, request.To, cancellationToken))
            : Result.Failure<OutputTaxReport>(TaxReports.NoFirm<OutputTaxReport>().Error);
    }
}

/// <summary>Handles <see cref="GetInputTaxQuery"/>.</summary>
public sealed class GetInputTaxQueryHandler : IQueryHandler<GetInputTaxQuery, InputTaxReport>
{
    private readonly ITaxReturnReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetInputTaxQueryHandler"/> class.</summary>
    /// <param name="reader">The tax return reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetInputTaxQueryHandler(ITaxReturnReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<InputTaxReport>> Handle(
        GetInputTaxQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is { } firmId
            ? Result.Success(
                await _reader.ReadInputAsync(firmId, request.From, request.To, cancellationToken))
            : Result.Failure<InputTaxReport>(TaxReports.NoFirm<InputTaxReport>().Error);
    }
}

/// <summary>Handles <see cref="GetTaxSummaryQuery"/>.</summary>
public sealed class GetTaxSummaryQueryHandler : IQueryHandler<GetTaxSummaryQuery, TaxSummaryReport>
{
    private readonly ITaxReturnReader _reader;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initialises a new instance of the <see cref="GetTaxSummaryQueryHandler"/> class.</summary>
    /// <param name="reader">The tax return reader.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    public GetTaxSummaryQueryHandler(ITaxReturnReader reader, ITenantContext tenantContext)
    {
        _reader = reader;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<TaxSummaryReport>> Handle(
        GetTaxSummaryQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _tenantContext.FirmId is { } firmId
            ? Result.Success(
                await _reader.ReadSummaryAsync(firmId, request.From, request.To, cancellationToken))
            : Result.Failure<TaxSummaryReport>(TaxReports.NoFirm<TaxSummaryReport>().Error);
    }
}

/// <summary>The refusal the three tax reports share.</summary>
internal static class TaxReports
{
    /// <summary>A tax return belongs to a firm, and no firm is selected.</summary>
    /// <typeparam name="TReport">The report that was asked for.</typeparam>
    /// <returns>The failure.</returns>
    internal static Result<TReport> NoFirm<TReport>() =>
        Result.Failure<TReport>(Error.Forbidden(
            "TaxReturn.NoFirmSelected",
            "A firm must be selected to read a tax return: the regime, the accounts and "
            + "the documents all belong to one."));
}
