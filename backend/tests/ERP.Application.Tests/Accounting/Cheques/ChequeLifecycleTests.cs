using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Accounting.Cheques;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Numbering;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Accounting.Cheques;

/// <summary>
/// Tests for the cheque lifecycle handlers.
/// </summary>
/// <remarks>
/// The aggregate proves the state machine. What is proved here is everything around
/// it: that a cheque cannot be worked on from another firm's books, that a clearing
/// voucher is real and posted before a cheque is tied to it, and - the one that
/// matters most - that a bounce puts the settled bills back rather than leaving the
/// books claiming an invoice was paid by a cheque the bank returned.
/// </remarks>
public sealed class ChequeLifecycleTests
{
    private static readonly DateOnly Matures = new(2026, 6, 1);

    // ------------------------------------------------------------ banking

    [Fact]
    public async Task A_cheque_is_banked_into_the_named_account()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(1_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new DepositChequeCommand(cheque.Id.Value, fixture.Bank.Id.Value, Matures));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ChequeStatus.Deposited);
        cheque.BankLedgerId.ShouldBe(fixture.Bank.Id);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Banking_a_post_dated_cheque_early_is_refused()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(1_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new DepositChequeCommand(
                cheque.Id.Value, fixture.Bank.Id.Value, Matures.AddDays(-1)));

        result.Error.Code.ShouldBe("Cheque.BankedBeforeItsDate");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cheque_cannot_be_banked_into_something_that_is_not_a_bank()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(1_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new DepositChequeCommand(cheque.Id.Value, fixture.Customer.Id.Value, Matures));

        result.Error.Code.ShouldBe("Cheque.NotABankAccount");
    }

    // ------------------------------------------------------------ scope guards

    [Fact]
    public async Task A_cheque_in_a_sibling_firms_books_cannot_be_touched()
    {
        // Tenant isolation permits reading it - the firms share a tenant. Marking it
        // cleared from the wrong set of books would take a payment off a register
        // nobody was looking at.
        Fixture fixture = new();
        Cheque foreign = fixture.ChequeInAnotherFirm(500m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new DepositChequeCommand(foreign.Id.Value, fixture.Bank.Id.Value, Matures));

        result.Error.Code.ShouldBe("Cheque.NotFound");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Working_with_cheques_without_a_firm_selected_is_refused()
    {
        Fixture fixture = new(firmSelected: false);
        Cheque cheque = fixture.PendingReceived(1_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new DepositChequeCommand(cheque.Id.Value, fixture.Bank.Id.Value, Matures));

        result.Error.Code.ShouldBe("Cheque.NoFirmSelected");
        result.Error.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    // ------------------------------------------------------------ clearing

    [Fact]
    public async Task Clearing_ties_the_cheque_to_the_voucher_that_posted_the_movement()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(1_000m);
        Voucher posting = fixture.PostedVoucher();

        Result<ChequeStateResponse> result = await fixture.Handle(
            new ClearChequeCommand(cheque.Id.Value, Matures.AddDays(2), posting.Id.Value));

        result.IsSuccess.ShouldBeTrue();
        cheque.Status.ShouldBe(ChequeStatus.Cleared);
        cheque.ClearingVoucherId.ShouldBe(posting.Id);
        cheque.ClearedOn.ShouldBe(Matures.AddDays(2));
    }

    [Fact]
    public async Task A_clearing_voucher_that_does_not_exist_is_refused()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(1_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new ClearChequeCommand(cheque.Id.Value, Matures, Guid.CreateVersion7()));

        result.Error.Code.ShouldBe("Cheque.ClearingVoucherNotFound");
    }

    [Fact]
    public async Task A_draft_voucher_cannot_account_for_a_cleared_cheque()
    {
        // It is not in the books, so a bank reconciliation pointed at it would find
        // nothing - exactly when somebody is looking, because the reconciliation
        // will not balance.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(1_000m);
        Voucher draft = fixture.DraftVoucher();

        Result<ChequeStateResponse> result = await fixture.Handle(
            new ClearChequeCommand(cheque.Id.Value, Matures, draft.Id.Value));

        result.Error.Code.ShouldBe("Cheque.ClearingVoucherNotPosted");
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
    }

    // ------------------------------------------------------------ bouncing

    [Fact]
    public async Task A_bounce_puts_back_the_bills_its_receipt_had_settled()
    {
        // The one that matters. Without it the books would go on claiming an invoice
        // was paid by a cheque the bank returned, and the customer's outstanding
        // would understate what they owe until somebody chased a payment that never
        // really happened.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(1_000m);
        Bill invoice = fixture.BillSettledBy(cheque, "INV-700", raised: 1_000m);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(
                cheque.Id.Value, "Insufficient funds", Matures.AddDays(3)));

        result.IsSuccess.ShouldBeTrue();
        cheque.Status.ShouldBe(ChequeStatus.Bounced);

        invoice.Status.ShouldBe(BillStatus.Open);
        invoice.OutstandingAmount.Amount.ShouldBe(1_000m);

        ReopenedBill reopened = result.Value.BillsReopened.ShouldHaveSingleItem();
        reopened.BillNumber.ShouldBe("INV-700");
        reopened.AmountReleased.ShouldBe(1_000m);
        reopened.OutstandingAmount.ShouldBe(1_000m);
        result.Value.AmountReopened.ShouldBe(1_000m);
    }

    [Fact]
    public async Task A_bounce_writes_its_own_reversing_journal()
    {
        // The business's answer of 2026-08-10, and the reason this test no longer says
        // a reversal is owed: which accounts a dishonour posts to stopped being a
        // question the moment they named them, so a bounce that left the books alone
        // stopped being caution and became an unfinished job.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Refer to drawer", Matures));

        result.Value.LedgerReversalRequired.ShouldBeFalse();
        cheque.ReversalVoucherId.ShouldNotBeNull();

        Voucher journal = fixture.Posted.ShouldHaveSingleItem();

        journal.Status.ShouldBe(VoucherStatus.Posted);
        Fixture.Debited(journal, fixture.Customer.Id).ShouldBe(500m);
        Fixture.Credited(journal, fixture.ChequesInHand!.Id).ShouldBe(500m);
    }

    [Fact]
    public async Task The_bank_s_charge_is_posted_with_the_bounce_when_one_is_stated()
    {
        // One posting for the whole event. The fee usually arrives on the same advice
        // as the return, and splitting it into a second journal is how it gets missed.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        await fixture.HandleBounce(
            new BounceChequeCommand(
                cheque.Id.Value, "Refer to drawer", Matures, BankCharge: 25m));

        Voucher journal = fixture.Posted.ShouldHaveSingleItem();

        Fixture.Debited(journal, fixture.BankCharges!.Id).ShouldBe(25m);
        Fixture.Credited(journal, fixture.Bank.Id).ShouldBe(25m);

        // And it still balances with both movements on it.
        Fixture.Balances(journal).ShouldBeTrue();
    }

    [Fact]
    public async Task A_bounce_with_no_charge_stated_posts_only_the_cheque()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Refer to drawer", Matures));

        Voucher journal = fixture.Posted.ShouldHaveSingleItem();

        journal.Lines.Count.ShouldBe(2);
        Fixture.Debited(journal, fixture.BankCharges!.Id).ShouldBe(0m);
    }

    [Fact]
    public async Task A_firm_that_has_not_chosen_the_accounts_cannot_bounce_a_cheque()
    {
        // The same refusal stock postings give, and for the same reason: a dishonour
        // posted into an account nobody chose is found at a reconciliation months
        // later, where one refused is found now by somebody who can fix it.
        Fixture fixture = new(accountsConfigured: false);
        Cheque cheque = fixture.BankedReceived(500m);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Refer to drawer", Matures));

        result.Error.Code.ShouldBe("InventoryAccounts.NotConfigured");
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
    }

    [Fact]
    public async Task A_bounce_that_names_its_reversal_owes_nothing_further()
    {
        // The other half of the same rule. Nothing here invents a posting, but where
        // an operator has already written one, the cheque records which it was and
        // stops reporting a debt to the books that has been paid.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);
        Voucher reversal = fixture.PostedVoucher();

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(
                cheque.Id.Value, "Refer to drawer", Matures, reversal.Id.Value));

        result.Value.LedgerReversalRequired.ShouldBeFalse();
        cheque.ReversalVoucherId.ShouldBe(reversal.Id);
    }

    [Fact]
    public async Task A_reversal_can_be_attached_to_a_bounce_that_has_none()
    {
        // Still reachable, and still worth having: a cheque bounced before the journal
        // was raised automatically has no reversal named, and this is how somebody
        // points it at the entry they wrote by hand at the time. A bounce recorded
        // today names its own, so this is the older rows' route rather than the
        // ordinary one it used to be.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedBouncedWithoutReversal(500m);

        cheque.ReversalVoucherId.ShouldBeNull();

        Voucher reversal = fixture.PostedVoucher();

        Result<ChequeStateResponse> result = await fixture.HandleReversal(
            new RecordChequeReversalCommand(cheque.Id.Value, reversal.Id.Value));

        result.IsSuccess.ShouldBeTrue();
        cheque.ReversalVoucherId.ShouldBe(reversal.Id);

        // Named once and not swapped afterwards: a register that pointed at one
        // reversal on Monday and another on Tuesday could not explain itself.
        Result<ChequeStateResponse> again = await fixture.HandleReversal(
            new RecordChequeReversalCommand(cheque.Id.Value, fixture.PostedVoucher().Id.Value));

        again.Error.Code.ShouldBe("Cheque.AlreadyReversed");
    }

    [Fact]
    public async Task A_reversal_must_be_posted_and_must_touch_the_party()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        // A draft is not in the books, so it accounts for nothing.
        Result<BouncedChequeResponse> draft = await fixture.HandleBounce(
            new BounceChequeCommand(
                cheque.Id.Value, "Refer to drawer", Matures, fixture.DraftVoucher().Id.Value));

        draft.Error.Code.ShouldBe("Cheque.ReversalVoucherNotPosted");

        // A voucher that never touches the customer cannot be undoing their receipt,
        // whatever its narration says.
        Result<BouncedChequeResponse> elsewhere = await fixture.HandleBounce(
            new BounceChequeCommand(
                cheque.Id.Value,
                "Refer to drawer",
                Matures,
                fixture.PostedVoucherWithoutTheParty().Id.Value));

        elsewhere.Error.Code.ShouldBe("Cheque.ReversalVoucherWrongParty");

        // Refused whole: the cheque did not bounce on the way past either failure.
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_a_bounced_cheque_can_name_a_reversal()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        Result<ChequeStateResponse> result = await fixture.HandleReversal(
            new RecordChequeReversalCommand(
                cheque.Id.Value, fixture.PostedVoucher().Id.Value));

        result.Error.Code.ShouldBe("Cheque.NotBounced");
    }

    [Fact]
    public async Task A_bounce_on_a_receipt_that_settled_nothing_reopens_nothing()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(500m);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Account closed", Matures));

        result.Value.BillsReopened.ShouldBeEmpty();
        result.Value.AmountReopened.ShouldBe(0m);
    }

    [Fact]
    public async Task A_partly_settled_bill_gives_back_only_this_receipts_share()
    {
        // Another receipt's allocation stands. Releasing it too would report a debt
        // the customer had genuinely paid.
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(400m);
        Bill invoice = fixture.BillSettledBy(cheque, "INV-701", raised: 1_000m, settled: 400m);
        invoice.Allocate(VoucherId.NewId(), Money.Of(250m, CurrencyCode.Qar), Matures);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Insufficient funds", Matures));

        result.Value.AmountReopened.ShouldBe(400m);
        invoice.SettledAmount.Amount.ShouldBe(250m);
        invoice.Status.ShouldBe(BillStatus.PartiallySettled);
    }

    [Fact]
    public async Task A_cheque_still_in_hand_cannot_bounce()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(500m);

        Result<BouncedChequeResponse> result = await fixture.HandleBounce(
            new BounceChequeCommand(cheque.Id.Value, "Insufficient funds", Matures));

        result.Error.Code.ShouldBe("Cheque.NotDeposited");
        await fixture.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ stopping and voiding

    [Fact]
    public async Task Payment_can_be_stopped_on_an_issued_cheque()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingIssued(2_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new StopChequeCommand(cheque.Id.Value, "Goods never delivered", Matures));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ChequeStatus.Stopped);
        cheque.ClosureReason.ShouldBe("Goods never delivered");
    }

    [Fact]
    public async Task A_received_cheque_cannot_be_stopped()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(2_000m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new StopChequeCommand(cheque.Id.Value, "Changed our minds", Matures));

        result.Error.Code.ShouldBe("Cheque.OnlyIssuedCanBeStopped");
    }

    [Fact]
    public async Task A_cheque_in_hand_can_be_voided()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.PendingReceived(750m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new CancelChequeCommand(cheque.Id.Value, "Replaced with a transfer", Matures));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ChequeStatus.Cancelled);
    }

    [Fact]
    public async Task A_cheque_with_the_bank_cannot_be_voided()
    {
        Fixture fixture = new();
        Cheque cheque = fixture.BankedReceived(750m);

        Result<ChequeStateResponse> result = await fixture.Handle(
            new CancelChequeCommand(cheque.Id.Value, "Changed our minds", Matures));

        result.Error.Code.ShouldBe("Cheque.NotPending");
    }

    /// <summary>A register of cheques and the bills they settled, over substitutes.</summary>
    private sealed class Fixture
    {
        private readonly Dictionary<ChequeId, Cheque> _cheques = [];
        private readonly Dictionary<VoucherId, Voucher> _vouchers = [];
        private readonly List<Bill> _bills = [];

        private readonly DepositChequeCommandHandler _deposit;
        private readonly ClearChequeCommandHandler _clear;
        private readonly BounceChequeCommandHandler _bounce;
        private readonly RecordChequeReversalCommandHandler _reverse;
        private readonly StopChequeCommandHandler _stop;
        private readonly CancelChequeCommandHandler _cancel;

        internal Fixture(bool firmSelected = true, bool accountsConfigured = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                TenantId.NewId(), "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            BranchId = SharedKernel.Tenancy.BranchId.NewId();

            Year = FinancialYear.Create(
                Firm.TenantId, Firm.Id, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            Customer = LedgerIn(Firm, "2000", "Al Mansoor", LedgerKind.Customer);
            Bank = LedgerIn(Firm, "1100", "Bank current", LedgerKind.Bank);

            IChequeRepository cheques = Substitute.For<IChequeRepository>();
            cheques
                .FindAsync(Arg.Any<ChequeId>(), Arg.Any<CancellationToken>())
                .Returns(call => _cheques.GetValueOrDefault(call.Arg<ChequeId>()));

            ILedgerRepository ledgers = Substitute.For<ILedgerRepository>();
            ledgers
                .FindAsync(Arg.Any<LedgerId>(), Arg.Any<CancellationToken>())
                .Returns(call => LedgerFor(call.Arg<LedgerId>()));

            IVoucherRepository vouchers = Substitute.For<IVoucherRepository>();
            vouchers
                .FindAsync(Arg.Any<VoucherId>(), Arg.Any<CancellationToken>())
                .Returns(call => _vouchers.GetValueOrDefault(call.Arg<VoucherId>()));
            vouchers.When(v => v.Add(Arg.Any<Voucher>()))
                .Do(call => Posted.Add(call.Arg<Voucher>()!));

            IBillRepository bills = Substitute.For<IBillRepository>();
            bills
                .FindAllocatedByAsync(Arg.Any<VoucherId>(), Arg.Any<CancellationToken>())
                .Returns(call => AllocatedBy(call.Arg<VoucherId>()));

            UnitOfWork = Substitute.For<IUnitOfWork>();

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Firm.TenantId);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);
            tenant.BranchId.Returns(firmSelected ? BranchId : null);

            // The accounts a bounce now posts through. Assigned unless a test asks for a
            // firm that has not chosen them, which is one of the refusals worth having.
            Accounts = InventoryAccountMap.Create(Firm.TenantId, Firm.Id);

            if (accountsConfigured)
            {
                ChequesInHand = LedgerIn(Firm, "1120", "Cheques in Hand", LedgerKind.General);
                BankCharges = LedgerIn(Firm, "5910", "Bank Charges", LedgerKind.General);

                Accounts.Assign(StockAccount.ChequesInHand, ChequesInHand).IsSuccess
                    .ShouldBeTrue();
                Accounts.Assign(StockAccount.BankCharges, BankCharges).IsSuccess.ShouldBeTrue();
            }

            IInventoryAccountMapRepository accounts =
                Substitute.For<IInventoryAccountMapRepository>();

            accounts.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Accounts);

            INumberingSeriesRepository numbering = Substitute.For<INumberingSeriesRepository>();
            numbering
                .FindForUpdateAsync(
                    Arg.Any<string>(), Arg.Any<FirmId>(), Arg.Any<BranchId>(),
                    Arg.Any<FinancialYearId>(), Arg.Any<CancellationToken>())
                .Returns((NumberingSeries?)null);
            numbering.When(n => n.Add(Arg.Any<NumberingSeries>()))
                .Do(call => Series.Add(call.Arg<NumberingSeries>()!));

            IFinancialYearRepository financialYears =
                Substitute.For<IFinancialYearRepository>();

            financialYears
                .FindContainingAsync(
                    Arg.Any<FirmId>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(Year);

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            ICurrentUser user = Substitute.For<ICurrentUser>();
            user.UserId.Returns(UserId.NewId());

            IClock clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

            _deposit = new DepositChequeCommandHandler(cheques, ledgers, tenant, UnitOfWork);
            _clear = new ClearChequeCommandHandler(cheques, vouchers, tenant, UnitOfWork);
            _bounce = new BounceChequeCommandHandler(
                cheques, bills, vouchers, accounts, numbering, financialYears, firms, tenant,
                user, clock, UnitOfWork);
            _reverse = new RecordChequeReversalCommandHandler(
                cheques, vouchers, tenant, UnitOfWork);
            _stop = new StopChequeCommandHandler(cheques, tenant, UnitOfWork);
            _cancel = new CancelChequeCommandHandler(cheques, tenant, UnitOfWork);
        }

        internal Firm Firm { get; }

        internal BranchId BranchId { get; }

        internal FinancialYear Year { get; }

        /// <summary>Gets the firm's account map, as a bounce reads it.</summary>
        internal InventoryAccountMap Accounts { get; }

        /// <summary>Gets the account a cheque waits in, where the firm has chosen one.</summary>
        internal Ledger? ChequesInHand { get; }

        /// <summary>Gets the account the bank's charges land in.</summary>
        internal Ledger? BankCharges { get; }

        /// <summary>Gets the numbering series the bounce created for its journal.</summary>
        internal List<NumberingSeries> Series { get; } = [];

        /// <summary>Gets the journals the handlers posted, in order.</summary>
        internal List<Voucher> Posted { get; } = [];

        internal Ledger Customer { get; }

        internal Ledger Bank { get; }

        internal IUnitOfWork UnitOfWork { get; }

        /// <summary>Registers a received cheque still in hand.</summary>
        internal Cheque PendingReceived(decimal amount) => Register(Cheque.Record(
            Firm.TenantId, Firm.Id, ChequeDirection.Received, Customer.Id,
            VoucherId.NewId(), "100201", Matures, Matures.AddMonths(-1),
            Money.Of(amount, CurrencyCode.Qar), drawnOnBank: "Qatar National Bank").Value);

        /// <summary>Registers an issued cheque still in hand.</summary>
        internal Cheque PendingIssued(decimal amount) => Register(Cheque.Record(
            Firm.TenantId, Firm.Id, ChequeDirection.Issued, Customer.Id,
            VoucherId.NewId(), "000501", Matures, Matures.AddMonths(-1),
            Money.Of(amount, CurrencyCode.Qar), bankLedgerId: Bank.Id).Value);

        /// <summary>Registers a received cheque already paid in.</summary>
        internal Cheque BankedReceived(decimal amount)
        {
            Cheque cheque = PendingReceived(amount);
            cheque.Deposit(Bank.Id, Matures).IsSuccess.ShouldBeTrue();

            return cheque;
        }

        /// <summary>A cheque already bounced without a reversal, as older rows are.</summary>
        /// <remarks>
        /// Bounced on the aggregate rather than through the handler, because the handler
        /// now always writes a journal - and this is precisely the state that predates
        /// it: a dishonour recorded when the books were still somebody else's job.
        /// </remarks>
        internal Cheque BankedBouncedWithoutReversal(decimal amount)
        {
            Cheque cheque = BankedReceived(amount);

            cheque.Bounce("Insufficient funds", Matures).IsSuccess.ShouldBeTrue();

            return cheque;
        }

        /// <summary>What one ledger was debited on a journal.</summary>
        internal static decimal Debited(Voucher journal, LedgerId ledgerId) =>
            journal.Lines
                .Where(line => line.LedgerId == ledgerId && line.Side == EntrySide.Debit)
                .Sum(line => line.Amount.Amount);

        /// <summary>What one ledger was credited.</summary>
        internal static decimal Credited(Voucher journal, LedgerId ledgerId) =>
            journal.Lines
                .Where(line => line.LedgerId == ledgerId && line.Side == EntrySide.Credit)
                .Sum(line => line.Amount.Amount);

        /// <summary>Whether a journal's two sides agree.</summary>
        internal static bool Balances(Voucher journal) =>
            journal.Lines.Where(line => line.Side == EntrySide.Debit)
                .Sum(line => line.Amount.Amount)
            == journal.Lines.Where(line => line.Side == EntrySide.Credit)
                .Sum(line => line.Amount.Amount);

        /// <summary>Registers a cheque belonging to a sibling firm of the same tenant.</summary>
        internal Cheque ChequeInAnotherFirm(decimal amount) => Register(Cheque.Record(
            Firm.TenantId, FirmId.NewId(), ChequeDirection.Received, Customer.Id,
            VoucherId.NewId(), "999999", Matures, Matures.AddMonths(-1),
            Money.Of(amount, CurrencyCode.Qar)).Value);

        /// <summary>Registers a bill the cheque's own receipt allocated against.</summary>
        /// <param name="cheque">The cheque whose receipt settled it.</param>
        /// <param name="number">The bill reference.</param>
        /// <param name="raised">The amount the bill was raised for.</param>
        /// <param name="settled">
        /// What that receipt allocated. Defaults to the whole bill.
        /// </param>
        /// <returns>The bill.</returns>
        internal Bill BillSettledBy(
            Cheque cheque, string number, decimal raised, decimal? settled = null)
        {
            Bill bill = Bill.Raise(
                Firm.TenantId, Firm.Id, Customer.Id, VoucherId.NewId(),
                BillType.Receivable, number, Matures.AddMonths(-2), 30,
                Money.Of(raised, CurrencyCode.Qar)).Value;

            bill.Allocate(
                cheque.OriginVoucherId,
                Money.Of(settled ?? raised, CurrencyCode.Qar),
                Matures.AddMonths(-1)).IsSuccess.ShouldBeTrue();

            _bills.Add(bill);

            return bill;
        }

        /// <summary>Registers a posted voucher a cheque may clear against.</summary>
        internal Voucher PostedVoucher()
        {
            Voucher voucher = NewVoucher("BR/2026/0002");
            voucher.AddLine(Bank.Id, EntrySide.Debit, 1_000m);
            voucher.AddLine(Customer.Id, EntrySide.Credit, 1_000m);
            voucher.Post(UserId.NewId(), DateTimeOffset.UnixEpoch).IsSuccess.ShouldBeTrue();

            return voucher;
        }

        /// <summary>Registers an unposted voucher.</summary>
        internal Voucher DraftVoucher() => NewVoucher("BR/2026/0003");

        /// <summary>Registers a posted voucher that never touches the party.</summary>
        internal Voucher PostedVoucherWithoutTheParty()
        {
            Ledger charges = LedgerIn(Firm, "5200", "Bank charges", LedgerKind.General);

            Voucher voucher = NewVoucher($"JV/2026/{_vouchers.Count:0000}");
            voucher.AddLine(charges.Id, EntrySide.Debit, 25m);
            voucher.AddLine(Bank.Id, EntrySide.Credit, 25m);
            voucher.Post(UserId.NewId(), DateTimeOffset.UnixEpoch).IsSuccess.ShouldBeTrue();

            return voucher;
        }

        internal Task<Result<ChequeStateResponse>> Handle(DepositChequeCommand command) =>
            _deposit.Handle(command, TestContext.Current.CancellationToken);

        internal Task<Result<ChequeStateResponse>> Handle(ClearChequeCommand command) =>
            _clear.Handle(command, TestContext.Current.CancellationToken);

        internal Task<Result<ChequeStateResponse>> Handle(StopChequeCommand command) =>
            _stop.Handle(command, TestContext.Current.CancellationToken);

        internal Task<Result<ChequeStateResponse>> Handle(CancelChequeCommand command) =>
            _cancel.Handle(command, TestContext.Current.CancellationToken);

        /// <summary>
        /// Dispatches a bounce. Named apart from the other four because it alone
        /// returns what the bounce undid.
        /// </summary>
        internal Task<Result<BouncedChequeResponse>> HandleBounce(BounceChequeCommand command) =>
            _bounce.Handle(command, TestContext.Current.CancellationToken);

        internal Task<Result<ChequeStateResponse>> HandleReversal(
            RecordChequeReversalCommand command) =>
            _reverse.Handle(command, TestContext.Current.CancellationToken);

        private static Ledger LedgerIn(Firm firm, string code, string name, LedgerKind kind)
        {
            AccountGroup group = AccountGroup.CreateRoot(
                firm.TenantId, firm.Id, $"G{code}", $"Group {code}", AccountNature.Asset).Value;

            return Ledger.Create(group, code, name, kind, firm.BaseCurrency).Value;
        }

        private Cheque Register(Cheque cheque)
        {
            _cheques[cheque.Id] = cheque;

            return cheque;
        }

        /// <summary>Resolves one of the fixture's two ledgers by identifier.</summary>
        private Ledger? LedgerFor(LedgerId id)
        {
            if (id == Bank.Id)
            {
                return Bank;
            }

            return id == Customer.Id ? Customer : null;
        }

        private Voucher NewVoucher(string number)
        {
            FinancialYear year = FinancialYear.Create(
                Firm.TenantId, Firm.Id, "2026",
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

            Voucher voucher = Voucher.CreateDraft(
                Firm.TenantId, Firm.Id, BranchId.NewId(), year, VoucherType.BankReceipt,
                number, Matures, CurrencyCode.Qar, CurrencyCode.Qar, 1m).Value;

            _vouchers[voucher.Id] = voucher;

            return voucher;
        }

        private List<Bill> AllocatedBy(VoucherId voucherId) =>
            [.. _bills.Where(b => b.Allocations.Any(a => a.VoucherId == voucherId))];
    }
}
