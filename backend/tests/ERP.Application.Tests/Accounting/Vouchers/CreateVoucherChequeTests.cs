using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;

namespace ERP.Application.Tests.Accounting.Vouchers;

/// <summary>
/// Tests for the cheque half of <see cref="CreateVoucherCommandHandler"/>.
/// </summary>
/// <remarks>
/// The <see cref="Cheque"/> aggregate proves its own lifecycle; what it cannot prove
/// is that a posting files a cheque the right way round. A receipt recorded as a
/// payment would appear on the wrong half of the PDC report and be chased from the
/// wrong end - and nothing in the ledger balances would look wrong while it did.
/// </remarks>
public sealed class CreateVoucherChequeTests
{
    private static readonly DateOnly PostingDate = Fixture.PostingDate;
    private static readonly DateOnly NextMonth = Fixture.PostingDate.AddMonths(1);

    // ------------------------------------------------------------ recording

    [Fact]
    public async Task A_cheque_taken_from_a_customer_is_recorded_as_received()
    {
        // Crediting the customer discharges what they owe, which is what taking a
        // cheque in does. The direction comes from that, not from the ledger's kind.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 5_000m),
            CreditWithCheques(fixture.CustomerLedger, 5_000m, Cheque("100201", 5_000m))));

        result.IsSuccess.ShouldBeTrue();

        Cheque cheque = fixture.Recorded.ShouldHaveSingleItem();
        cheque.Direction.ShouldBe(ChequeDirection.Received);
        cheque.PartyLedgerId.ShouldBe(fixture.CustomerLedger.Id);
        cheque.ChequeNumber.ShouldBe("100201");
        cheque.Amount.Amount.ShouldBe(5_000m);
        cheque.Status.ShouldBe(ChequeStatus.Pending);
    }

    [Fact]
    public async Task A_cheque_written_to_a_supplier_is_recorded_as_issued()
    {
        Fixture fixture = new();

        await fixture.Handle(Command(
            DebitWithCheques(
                fixture.SupplierLedger, 3_000m,
                Cheque("000501", 3_000m) with { BankLedgerId = fixture.BankLedger.Id.Value }),
            Credit(fixture.BankLedger, 3_000m)));

        Cheque cheque = fixture.Recorded.ShouldHaveSingleItem();
        cheque.Direction.ShouldBe(ChequeDirection.Issued);
        cheque.BankLedgerId.ShouldBe(fixture.BankLedger.Id);
    }

    [Fact]
    public async Task A_post_dated_cheque_is_recorded_like_any_other()
    {
        // There is no separate kind of record. What makes it post-dated is only that
        // its date has not arrived, which the register derives.
        Fixture fixture = new();

        await fixture.Handle(Command(
            Debit(fixture.CashLedger, 5_000m),
            CreditWithCheques(
                fixture.CustomerLedger, 5_000m, Cheque("100202", 5_000m, NextMonth))));

        Cheque cheque = fixture.Recorded.ShouldHaveSingleItem();
        cheque.InstrumentDate.ShouldBe(NextMonth);
        cheque.RecordedOn.ShouldBe(PostingDate);
        cheque.IsPostDatedAt(PostingDate).ShouldBeTrue();
        cheque.IsPostDatedAt(NextMonth).ShouldBeFalse();
    }

    [Fact]
    public async Task One_line_can_carry_several_cheques()
    {
        // The common case for a post-dated arrangement: a customer settles a large
        // invoice with a run of cheques dated a month apart.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 9_000m),
            CreditWithCheques(
                fixture.CustomerLedger, 9_000m,
                Cheque("100301", 3_000m, NextMonth),
                Cheque("100302", 3_000m, NextMonth.AddMonths(1)),
                Cheque("100303", 3_000m, NextMonth.AddMonths(2)))));

        result.IsSuccess.ShouldBeTrue();
        fixture.Recorded.Count.ShouldBe(3);
        fixture.Recorded.Sum(c => c.Amount.Amount).ShouldBe(9_000m);
    }

    [Fact]
    public async Task The_bank_named_on_a_received_cheque_is_kept()
    {
        // It is how a bounced cheque gets chased.
        Fixture fixture = new();

        await fixture.Handle(Command(
            Debit(fixture.CashLedger, 500m),
            CreditWithCheques(
                fixture.CustomerLedger, 500m,
                Cheque("100204", 500m) with { DrawnOnBank = "Qatar National Bank" })));

        fixture.Recorded.ShouldHaveSingleItem().DrawnOnBank.ShouldBe("Qatar National Bank");
    }

    // ------------------------------------------------------------ shape

    [Fact]
    public async Task Cheques_that_do_not_cover_the_whole_line_are_refused()
    {
        // A part-covered line leaves a remainder settled by nothing in particular,
        // and the register stops reconciling with the postings behind it.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 1_000m),
            CreditWithCheques(fixture.CustomerLedger, 1_000m, Cheque("100205", 600m))));

        result.Error.Code.ShouldBe("Cheque.AmountsDoNotMatchLine");
        fixture.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cheque_cannot_be_recorded_against_the_firms_own_account()
    {
        // Without this, cheques attached to the bank line of a receipt would take
        // their direction from that line and file every incoming cheque as one the
        // firm had issued.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithCheques(fixture.BankLedger, 500m, Cheque("100206", 500m)),
            Credit(fixture.CustomerLedger, 500m)));

        result.Error.Code.ShouldBe("Cheque.NotAPartyLedger");
    }

    [Fact]
    public async Task An_issued_cheque_must_be_drawn_on_a_bank_account()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithCheques(
                fixture.SupplierLedger, 500m,
                Cheque("000502", 500m) with { BankLedgerId = fixture.SalesLedger.Id.Value }),
            Credit(fixture.BankLedger, 500m)));

        result.Error.Code.ShouldBe("Cheque.NotABankAccount");
    }

    [Fact]
    public async Task An_issued_cheque_with_no_account_named_is_refused()
    {
        // The domain's rule, surfaced through the handler: a bank reconciliation
        // that could not say which account a cheque will hit would be unusable.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithCheques(fixture.SupplierLedger, 500m, Cheque("000503", 500m)),
            Credit(fixture.BankLedger, 500m)));

        result.Error.Code.ShouldBe("Cheque.BankAccountRequired");
    }

    [Fact]
    public async Task A_number_the_party_already_has_live_is_refused()
    {
        Fixture fixture = new();
        fixture.ExistingCheque(
            fixture.CustomerLedger, ChequeDirection.Received, "100207", 200m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 200m),
            CreditWithCheques(fixture.CustomerLedger, 200m, Cheque("100207", 200m))));

        result.Error.Code.ShouldBe("Cheque.NumberAlreadyLive");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task The_same_number_twice_in_one_voucher_is_refused()
    {
        // No index catches this before the save, because neither row exists yet.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 400m),
            CreditWithCheques(
                fixture.CustomerLedger, 400m,
                Cheque("100208", 200m), Cheque("100208", 200m))));

        result.Error.Code.ShouldBe("Cheque.DuplicateNumberInVoucher");
    }

    [Fact]
    public async Task A_draft_cannot_carry_cheques()
    {
        // A cheque in the register that no posting accounts for would appear on the
        // PDC report as money expected against a receipt nobody made.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                Debit(fixture.CashLedger, 500m),
                CreditWithCheques(fixture.CustomerLedger, 500m, Cheque("100209", 500m))) with
            {
                PostImmediately = false,
            });

        result.Error.Code.ShouldBe("Cheque.DraftCannotCarryCheques");
        fixture.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_posting_with_no_cheques_records_none()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 500m),
            Credit(fixture.CustomerLedger, 500m)));

        result.IsSuccess.ShouldBeTrue();
        fixture.Recorded.ShouldBeEmpty();
    }

    // ------------------------------------------------------------ alongside bills

    [Fact]
    public async Task A_receipt_can_settle_bills_and_record_a_cheque_at_once()
    {
        // The realistic case, and the one where the two mechanisms have to agree:
        // the customer hands over a post-dated cheque against a named invoice.
        Fixture fixture = new();
        Bill invoice = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-500", 2_500m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 2_500m),
            new CreateVoucherLine(
                fixture.CustomerLedger.Id.Value,
                EntrySide.Credit,
                2_500m,
                BillReferences:
                [
                    new CreateVoucherBillReference(
                        BillReferenceKind.Against, 2_500m, BillId: invoice.Id.Value),
                ],
                Cheques: [Cheque("100210", 2_500m, NextMonth)])));

        result.IsSuccess.ShouldBeTrue();
        invoice.Status.ShouldBe(BillStatus.Settled);
        fixture.Recorded.ShouldHaveSingleItem().InstrumentDate.ShouldBe(NextMonth);
    }

    // ------------------------------------------------------------ helpers

    private static CreateVoucherCommand Command(params CreateVoucherLine[] lines) =>
        new(VoucherType.BankReceipt, PostingDate, lines);

    private static CreateVoucherLine Debit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Debit, amount);

    private static CreateVoucherLine Credit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Credit, amount);

    private static CreateVoucherLine DebitWithCheques(
        Ledger ledger, decimal amount, params CreateVoucherCheque[] cheques) =>
        new(ledger.Id.Value, EntrySide.Debit, amount, Cheques: cheques);

    private static CreateVoucherLine CreditWithCheques(
        Ledger ledger, decimal amount, params CreateVoucherCheque[] cheques) =>
        new(ledger.Id.Value, EntrySide.Credit, amount, Cheques: cheques);

    private static CreateVoucherCheque Cheque(
        string number, decimal amount, DateOnly? instrumentDate = null) =>
        new(number, instrumentDate ?? PostingDate, amount);
}
