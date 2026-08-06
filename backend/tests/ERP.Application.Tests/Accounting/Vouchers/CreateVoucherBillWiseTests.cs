using ERP.Application.Accounting.Vouchers;
using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;

namespace ERP.Application.Tests.Accounting.Vouchers;

/// <summary>
/// Tests for the bill-wise half of <see cref="CreateVoucherCommandHandler"/>.
/// </summary>
/// <remarks>
/// The <see cref="Bill"/> aggregate already proves its own arithmetic - what it
/// cannot prove is that the right bills are raised and settled by a posting. That is
/// where the two halves of the system meet, and where a mistake produces the worst
/// kind of discrepancy: a party's balance and their outstanding report disagreeing,
/// with neither figure looking obviously wrong on its own.
/// </remarks>
public sealed class CreateVoucherBillWiseTests
{
    private static readonly DateOnly PostingDate = Fixture.PostingDate;

    // ------------------------------------------------------------ raising bills

    [Fact]
    public async Task An_invoice_raises_a_receivable_against_the_customer()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.CustomerLedger, 1_200m, New("INV-001", 1_200m)),
            Credit(fixture.SalesLedger, 1_200m)));

        result.IsSuccess.ShouldBeTrue();

        Bill raised = fixture.Raised.ShouldHaveSingleItem();
        raised.Type.ShouldBe(BillType.Receivable);
        raised.LedgerId.ShouldBe(fixture.CustomerLedger.Id);
        raised.BillNumber.ShouldBe("INV-001");
        raised.OriginalAmount.Amount.ShouldBe(1_200m);
        raised.Status.ShouldBe(BillStatus.Open);
    }

    [Fact]
    public async Task A_purchase_raises_a_payable_against_the_supplier()
    {
        // The direction comes from the side, not from the ledger's kind: crediting a
        // party means the firm owes them, whoever they are.
        Fixture fixture = new();

        await fixture.Handle(Command(
            Debit(fixture.SalesLedger, 800m),
            CreditWithBills(fixture.SupplierLedger, 800m, New("GD-4471", 800m))));

        Bill raised = fixture.Raised.ShouldHaveSingleItem();
        raised.Type.ShouldBe(BillType.Payable);
        raised.LedgerId.ShouldBe(fixture.SupplierLedger.Id);
    }

    [Fact]
    public async Task The_due_date_comes_from_the_partys_credit_terms()
    {
        // 30-day terms on the customer, so an invoice dated 15 June falls due on
        // 15 July. Aging counts from that date, which makes it the figure the whole
        // report hangs on.
        Fixture fixture = new();

        await fixture.Handle(Command(
            DebitWithBills(fixture.CustomerLedger, 500m, New("INV-002", 500m)),
            Credit(fixture.SalesLedger, 500m)));

        fixture.Raised.ShouldHaveSingleItem().DueDate.ShouldBe(PostingDate.AddDays(30));
    }

    [Fact]
    public async Task Credit_terms_can_be_overridden_for_one_bill()
    {
        Fixture fixture = new();

        await fixture.Handle(Command(
            DebitWithBills(
                fixture.CustomerLedger, 500m, New("INV-003", 500m) with { CreditDays = 7 }),
            Credit(fixture.SalesLedger, 500m)));

        fixture.Raised.ShouldHaveSingleItem().DueDate.ShouldBe(PostingDate.AddDays(7));
    }

    [Fact]
    public async Task One_line_can_raise_several_bills()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(
                fixture.CustomerLedger, 900m, New("INV-004", 400m), New("INV-005", 500m)),
            Credit(fixture.SalesLedger, 900m)));

        result.IsSuccess.ShouldBeTrue();
        fixture.Raised.Count.ShouldBe(2);
        fixture.Raised.Sum(b => b.OriginalAmount.Amount).ShouldBe(900m);
    }

    // ------------------------------------------------------------ settling bills

    [Fact]
    public async Task A_receipt_settles_the_invoice_it_names()
    {
        Fixture fixture = new();
        Bill invoice = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-100", 1_000m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 1_000m),
            CreditWithBills(fixture.CustomerLedger, 1_000m, Against(invoice, 1_000m))));

        result.IsSuccess.ShouldBeTrue();
        invoice.Status.ShouldBe(BillStatus.Settled);
        invoice.OutstandingAmount.IsZero.ShouldBeTrue();
        fixture.Raised.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_part_payment_leaves_the_balance_outstanding()
    {
        // The case the whole feature exists for: a running balance says the customer
        // owes 600, but only bill-wise settlement says it is the remainder of
        // INV-100 rather than a newer invoice.
        Fixture fixture = new();
        Bill invoice = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-100", 1_000m);

        await fixture.Handle(Command(
            Debit(fixture.CashLedger, 400m),
            CreditWithBills(fixture.CustomerLedger, 400m, Against(invoice, 400m))));

        invoice.Status.ShouldBe(BillStatus.PartiallySettled);
        invoice.OutstandingAmount.Amount.ShouldBe(600m);
    }

    [Fact]
    public async Task One_receipt_can_clear_several_invoices()
    {
        Fixture fixture = new();
        Bill first = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-101", 300m);
        Bill second = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-102", 700m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.BankLedger, 1_000m),
            CreditWithBills(
                fixture.CustomerLedger, 1_000m, Against(first, 300m), Against(second, 700m))));

        result.IsSuccess.ShouldBeTrue();
        first.Status.ShouldBe(BillStatus.Settled);
        second.Status.ShouldBe(BillStatus.Settled);
    }

    [Fact]
    public async Task A_payment_settles_a_payable()
    {
        Fixture fixture = new();
        Bill purchase = fixture.ExistingBill(
            fixture.SupplierLedger, BillType.Payable, "GD-4471", 800m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.SupplierLedger, 800m, Against(purchase, 800m)),
            Credit(fixture.BankLedger, 800m)));

        result.IsSuccess.ShouldBeTrue();
        purchase.Status.ShouldBe(BillStatus.Settled);
    }

    [Fact]
    public async Task Over_allocating_a_bill_is_refused_and_nothing_is_saved()
    {
        // The domain refuses it; what this proves is that the handler surfaces the
        // refusal rather than saving a posting whose settlement never took.
        Fixture fixture = new();
        Bill invoice = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-103", 500m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 900m),
            CreditWithBills(fixture.CustomerLedger, 900m, Against(invoice, 900m))));

        result.Error.Code.ShouldBe("Bill.OverAllocated");
        invoice.Status.ShouldBe(BillStatus.Open);
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ misdirected settlement

    [Fact]
    public async Task A_receipt_cannot_settle_another_partys_bill()
    {
        // No index catches this and tenant isolation does not either - the bill is
        // perfectly readable. Allowing it would leave two parties' outstanding
        // figures wrong at once, and the ledger balances would still reconcile.
        Fixture fixture = new();
        Bill someoneElses = fixture.ExistingBill(
            fixture.SupplierLedger, BillType.Receivable, "INV-104", 200m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 200m),
            CreditWithBills(fixture.CustomerLedger, 200m, Against(someoneElses, 200m))));

        result.Error.Code.ShouldBe("Bill.WrongParty");
    }

    [Fact]
    public async Task A_receivable_cannot_be_settled_by_debiting_the_customer()
    {
        // Debiting a customer increases what they owe. Treating it as a settlement
        // would report an invoice as paid by a document that raised more debt.
        Fixture fixture = new();
        Bill invoice = fixture.ExistingBill(
            fixture.CustomerLedger, BillType.Receivable, "INV-105", 200m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.CustomerLedger, 200m, Against(invoice, 200m)),
            Credit(fixture.SalesLedger, 200m)));

        result.Error.Code.ShouldBe("Bill.WrongSideForSettlement");
    }

    [Fact]
    public async Task A_bill_that_does_not_exist_is_reported_rather_than_ignored()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 100m),
            CreditWithBills(
                fixture.CustomerLedger,
                100m,
                new CreateVoucherBillReference(
                    BillReferenceKind.Against, 100m, BillId: Guid.CreateVersion7()))));

        result.Error.Code.ShouldBe("Bill.NotFound");
    }

    // ------------------------------------------------------------ shape of the references

    [Fact]
    public async Task References_that_do_not_account_for_the_whole_line_are_refused()
    {
        // A partial breakdown leaves a remainder belonging to no bill, which is what
        // makes an outstanding report stop reconciling with the party's balance.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.CustomerLedger, 1_000m, New("INV-106", 600m)),
            Credit(fixture.SalesLedger, 1_000m)));

        result.Error.Code.ShouldBe("Bill.ReferencesDoNotMatchLine");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        fixture.Raised.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_posting_with_no_references_at_all_is_left_alone()
    {
        // An on-account receipt is legitimate. Only a partial breakdown is a mistake.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            Debit(fixture.CashLedger, 250m),
            Credit(fixture.CustomerLedger, 250m)));

        result.IsSuccess.ShouldBeTrue();
        fixture.Raised.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ledger_not_tracked_bill_wise_cannot_carry_references()
    {
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.CashLedger, 100m, New("INV-107", 100m)),
            Credit(fixture.SalesLedger, 100m)));

        result.Error.Code.ShouldBe("Bill.LedgerNotBillWise");
    }

    [Fact]
    public async Task A_reference_the_party_already_has_is_refused()
    {
        // The unique index would reject it anyway, as a 500. Naming the clash is the
        // difference between an operator fixing their own typo and raising a ticket.
        Fixture fixture = new();
        fixture.ExistingBill(fixture.CustomerLedger, BillType.Receivable, "INV-108", 100m);

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(fixture.CustomerLedger, 100m, New("INV-108", 100m)),
            Credit(fixture.SalesLedger, 100m)));

        result.Error.Code.ShouldBe("Bill.ReferenceAlreadyUsed");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task The_same_reference_twice_in_one_voucher_is_refused()
    {
        // No index catches this before the save, because neither row exists yet.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(Command(
            DebitWithBills(
                fixture.CustomerLedger, 200m, New("INV-109", 100m), New("INV-109", 100m)),
            Credit(fixture.SalesLedger, 200m)));

        result.Error.Code.ShouldBe("Bill.DuplicateReferenceInVoucher");
    }

    [Fact]
    public async Task A_draft_cannot_carry_bill_references()
    {
        // Allocations live on the bill, not the voucher, so there is nowhere to hold
        // them until somebody posts the draft. Refusing says so; accepting and
        // dropping them would not.
        Fixture fixture = new();

        Result<CreateVoucherResponse> result = await fixture.Handle(
            Command(
                DebitWithBills(fixture.CustomerLedger, 100m, New("INV-110", 100m)),
                Credit(fixture.SalesLedger, 100m)) with
            {
                PostImmediately = false,
            });

        result.Error.Code.ShouldBe("Bill.DraftCannotCarryReferences");
        fixture.Raised.ShouldBeEmpty();
    }

    // ------------------------------------------------------------ helpers

    /// <summary>A journal on the standard posting date, with the given lines.</summary>
    private static CreateVoucherCommand Command(params CreateVoucherLine[] lines) =>
        new(VoucherType.Journal, PostingDate, lines);

    private static CreateVoucherLine Debit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Debit, amount);

    private static CreateVoucherLine Credit(Ledger ledger, decimal amount) =>
        new(ledger.Id.Value, EntrySide.Credit, amount);

    private static CreateVoucherLine DebitWithBills(
        Ledger ledger, decimal amount, params CreateVoucherBillReference[] references) =>
        new(ledger.Id.Value, EntrySide.Debit, amount, BillReferences: references);

    private static CreateVoucherLine CreditWithBills(
        Ledger ledger, decimal amount, params CreateVoucherBillReference[] references) =>
        new(ledger.Id.Value, EntrySide.Credit, amount, BillReferences: references);

    private static CreateVoucherBillReference New(string billNumber, decimal amount) =>
        new(BillReferenceKind.New, amount, BillNumber: billNumber);

    private static CreateVoucherBillReference Against(Bill bill, decimal amount) =>
        new(BillReferenceKind.Against, amount, BillId: bill.Id.Value);
}
