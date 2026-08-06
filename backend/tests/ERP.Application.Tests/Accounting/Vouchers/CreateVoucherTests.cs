using ERP.Application.Abstractions.Persistence;
using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.Domain.Numbering;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Accounting.Vouchers;

/// <summary>
/// Tests for <see cref="CreateVoucherCommandHandler"/>.
/// </summary>
/// <remarks>
/// This is the handler that writes to the books, and it had no tests. The domain
/// proves a voucher balances; what was unproven is everything around that - that
/// the posting lands in the selected firm and branch, that a date outside any
/// open financial year is refused, that a ledger belonging to a sibling firm
/// cannot be posted to, and that nothing is saved when any of those fail.
/// </remarks>
public sealed class CreateVoucherTests
{
    private static readonly TenantId Tenant = Fixture.Tenant;
    private static readonly DateOnly PostingDate = Fixture.PostingDate;

    // ------------------------------------------------------------ happy path

    [Fact]
    public async Task A_balanced_voucher_is_posted_and_numbered()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                Debit(fixture.CashLedger, 500m),
                Credit(fixture.SalesLedger, 500m)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(VoucherStatus.Posted);
        result.Value.TotalDebit.ShouldBe(500m);
        result.Value.Number.ShouldNotBeNullOrWhiteSpace();

        fixture.Vouchers.Received(1).Add(Arg.Any<Voucher>());
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_voucher_can_be_left_as_a_draft()
    {
        // The clerk-enters, supervisor-posts workflow. A draft is not in the books,
        // so it must not be reported as posted.
        Fixture fixture = new();

        CreateVoucherResponse response = (await fixture.Handle(
            Command(
                Debit(fixture.CashLedger, 100m),
                Credit(fixture.SalesLedger, 100m)) with
            {
                PostImmediately = false,
            })).Value;

        response.Status.ShouldBe(VoucherStatus.Draft);
        fixture.Vouchers.Received(1).Add(Arg.Any<Voucher>());
    }

    [Fact]
    public async Task The_voucher_is_stamped_with_the_selected_firm_and_branch()
    {
        // The check that stops a posting landing in the wrong set of books.
        Fixture fixture = new();
        Voucher? saved = null;
        fixture.Vouchers.When(v => v.Add(Arg.Any<Voucher>())).Do(c => saved = c.Arg<Voucher>());

        await fixture.Handle(Command(
            Debit(fixture.CashLedger, 250m), Credit(fixture.SalesLedger, 250m)));

        saved.ShouldNotBeNull();
        saved.FirmId.ShouldBe(fixture.Firm.Id);
        saved.BranchId.ShouldBe(fixture.BranchId);
        saved.TenantId.ShouldBe(Tenant);
    }

    // ------------------------------------------------------------ scope guards

    [Fact]
    public async Task Posting_without_a_firm_and_branch_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(fixture.CashLedger, 1m), Credit(fixture.SalesLedger, 1m)));

        result.Error.Code.ShouldBe("Voucher.NoFirmOrBranchSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);

        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ledger_belonging_to_another_firm_cannot_be_posted_to()
    {
        // The subtle one. Tenant isolation does not catch this: a sibling firm in
        // the same tenant is perfectly readable, and posting to its ledger would
        // corrupt two sets of books at once.
        Fixture fixture = new();

        Firm otherFirm = Firm.Create(
            Tenant, "OTHER", "Other Firm", CurrencyCode.Qar,
            TaxRegime.GccVat, "Asia/Qatar").Value;

        Ledger foreignLedger = LedgerIn(otherFirm, "9000", "Someone else's cash", LedgerKind.Cash);
        fixture.RegisterLedger(foreignLedger);

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(foreignLedger, 100m), Credit(fixture.SalesLedger, 100m)));

        result.Error.Code.ShouldBe("Ledger.WrongFirm");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ledger_that_does_not_exist_is_reported_rather_than_ignored()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                new CreateVoucherLine(Guid.CreateVersion7(), EntrySide.Debit, 100m),
                Credit(fixture.SalesLedger, 100m)));

        result.Error.Code.ShouldBe("Ledger.NotFound");
    }

    // ------------------------------------------------------------ financial year

    [Fact]
    public async Task A_date_covered_by_no_financial_year_is_refused()
    {
        // Otherwise the posting would belong to no period, and every balance
        // derived from a period would quietly omit it.
        Fixture fixture = new();
        fixture.FinancialYears
            .FindContainingAsync(fixture.Firm.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((FinancialYear?)null);

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(fixture.CashLedger, 1m), Credit(fixture.SalesLedger, 1m)));

        result.Error.Code.ShouldBe("FinancialYear.NotFoundForDate");
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public async Task Posting_into_a_closed_year_is_refused()
    {
        // A closed period's statements have been published. Accepting a posting
        // into it would silently change figures somebody has already relied on.
        Fixture fixture = new();
        fixture.FinancialYear.Close();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(fixture.CashLedger, 1m), Credit(fixture.SalesLedger, 1m)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("FinancialYear.Closed");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ balance

    [Fact]
    public async Task An_unbalanced_voucher_is_refused_and_nothing_is_saved()
    {
        // The invariant the whole ledger rests on. It is enforced in the domain;
        // what this proves is that the handler surfaces the failure and does not
        // save regardless.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                Debit(fixture.CashLedger, 500m),
                Credit(fixture.SalesLedger, 400m)));

        result.IsFailure.ShouldBeTrue();
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_multi_line_voucher_balances_across_all_of_its_lines()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                Debit(fixture.CashLedger, 300m),
                Debit(fixture.BankLedger, 200m),
                Credit(fixture.SalesLedger, 500m)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalDebit.ShouldBe(500m);
    }

    // ------------------------------------------------------------ numbering

    [Fact]
    public async Task A_numbering_series_is_created_when_none_is_configured()
    {
        // A fresh installation has no series. Refusing to post until an
        // administrator visits a settings screen would make the system look broken.
        Fixture fixture = new();
        fixture.Numbering
            .FindForUpdateAsync(
                Arg.Any<string>(), Arg.Any<FirmId>(), Arg.Any<BranchId>(),
                Arg.Any<FinancialYearId>(), Arg.Any<CancellationToken>())
            .Returns((NumberingSeries?)null);

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(fixture.CashLedger, 10m), Credit(fixture.SalesLedger, 10m)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Number.ShouldNotBeNullOrWhiteSpace();
        fixture.Numbering.Received(1).Add(Arg.Any<NumberingSeries>());
    }

    [Fact]
    public async Task The_numbering_series_is_resolved_for_the_documents_own_type()
    {
        // A cash receipt and a journal keep separate sequences. Sharing one would
        // interleave their numbers and make either impossible to follow.
        Fixture fixture = new();

        await fixture.Handle(
            Command(Debit(fixture.CashLedger, 10m), Credit(fixture.SalesLedger, 10m))
                with { Type = VoucherType.Journal });

        await fixture.Numbering.Received(1).FindForUpdateAsync(
            DocumentTypes.Journal,
            fixture.Firm.Id,
            fixture.BranchId,
            fixture.FinancialYear.Id,
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ currency

    [Fact]
    public async Task An_unrecognised_currency_is_refused()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(Debit(fixture.CashLedger, 10m), Credit(fixture.SalesLedger, 10m))
                with { CurrencyCode = "NOTACURRENCY" });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Currency.Invalid");
    }

    [Fact]
    public async Task The_entry_currency_defaults_to_the_firms_base_currency()
    {
        Fixture fixture = new();
        Voucher? saved = null;
        fixture.Vouchers.When(v => v.Add(Arg.Any<Voucher>())).Do(c => saved = c.Arg<Voucher>());

        await fixture.Handle(
            Command(Debit(fixture.CashLedger, 10m), Credit(fixture.SalesLedger, 10m)));

        saved.ShouldNotBeNull();
        saved.Currency.ShouldBe(fixture.Firm.BaseCurrency);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>A cash receipt on the standard posting date, with the given lines.</summary>
    private static CreateVoucherCommand Command(params CreateVoucherLine[] lines) =>
        new(VoucherType.CashReceipt, PostingDate, lines);

    private static CreateVoucherLine Debit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Debit, amount);

    private static CreateVoucherLine Credit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Credit, amount);

    private static Ledger LedgerIn(Firm firm, string code, string name, LedgerKind kind) =>
        Fixture.LedgerIn(firm, code, name, kind);
}
