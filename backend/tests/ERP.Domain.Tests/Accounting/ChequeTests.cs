using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>
/// Tests for <see cref="Cheque"/>.
/// </summary>
/// <remarks>
/// A cheque is not the same thing as the money it promises, and the gap between the
/// two is where this aggregate earns its place. Between taking a cheque in and its
/// clearing there is a period - weeks, for a post-dated one - in which the firm
/// holds a claim that may yet fail, and no ledger balance distinguishes that from
/// cash in the bank. The PDC report, the PDC calendar, and the cheque register all
/// read the state machine below.
/// </remarks>
public sealed class ChequeTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly LedgerId Customer = LedgerId.NewId();
    private static readonly LedgerId BankAccount = LedgerId.NewId();
    private static readonly DateOnly Taken = new(2026, 4, 1);
    private static readonly DateOnly Matures = new(2026, 6, 1);
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;

    // ------------------------------------------------------------ recording

    [Fact]
    public void A_recorded_cheque_starts_in_hand()
    {
        Cheque cheque = Received(1000m);

        cheque.Status.ShouldBe(ChequeStatus.Pending);
        cheque.IsOutstanding.ShouldBeTrue();
        cheque.IsClosed.ShouldBeFalse();
        cheque.DepositedOn.ShouldBeNull();
        cheque.ClosedOn.ShouldBeNull();
        cheque.ClearedOn.ShouldBeNull();
    }

    [Fact]
    public void Being_post_dated_is_derived_from_the_date_on_its_face()
    {
        // Not stored. A flag set when the cheque was taken in would still be set the
        // day it matured, leaving every report to correct for it - and one of them
        // eventually would not.
        Cheque cheque = Received(1000m);

        cheque.IsPostDatedAt(Taken).ShouldBeTrue();
        cheque.IsPostDatedAt(Matures.AddDays(-1)).ShouldBeTrue();
        cheque.IsPostDatedAt(Matures).ShouldBeFalse();
        cheque.IsPostDatedAt(Matures.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void A_cheque_falls_due_on_the_date_it_bears()
    {
        // What the PDC calendar is read for: which cheques become bankable when.
        Cheque cheque = Received(1000m);

        cheque.IsDueAt(Matures.AddDays(-1)).ShouldBeFalse();
        cheque.IsDueAt(Matures).ShouldBeTrue();
    }

    [Fact]
    public void A_banked_cheque_is_no_longer_waiting_to_be_banked()
    {
        Cheque cheque = Received(1000m);
        cheque.Deposit(BankAccount, Matures);

        cheque.IsDueAt(Matures.AddDays(30)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_cheque_must_be_for_a_positive_amount(decimal amount)
    {
        // A refund is a cheque in the other direction, not a negative one -
        // otherwise the register's totals would net off against themselves.
        TryRecord(amount).Error.Code.ShouldBe("Cheque.AmountNotPositive");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_cheque_number_is_required(string number)
    {
        TryRecord(1000m, number: number).Error.Code.ShouldBe("Cheque.NumberRequired");
    }

    [Fact]
    public void A_cheque_number_is_bounded()
    {
        TryRecord(1000m, number: new string('9', Cheque.MaximumNumberLength + 1))
            .Error.Code.ShouldBe("Cheque.NumberTooLong");
    }

    [Fact]
    public void An_issued_cheque_must_name_the_account_it_is_drawn_on()
    {
        // A bank reconciliation that could not say which account a cheque will hit
        // would be unusable.
        Cheque.Record(
            Tenant, Firm, ChequeDirection.Issued, Customer, VoucherId.NewId(),
            "000123", Matures, Taken, Money.Of(1000m, Qar))
            .Error.Code.ShouldBe("Cheque.BankAccountRequired");
    }

    [Fact]
    public void A_received_cheque_needs_no_account_until_it_is_banked()
    {
        // It sits in hand until somebody decides which account to pay it into.
        Cheque cheque = Received(1000m);

        cheque.BankLedgerId.ShouldBeNull();

        cheque.Deposit(BankAccount, Matures).IsSuccess.ShouldBeTrue();
        cheque.BankLedgerId.ShouldBe(BankAccount);
    }

    // ------------------------------------------------------------ banking

    [Fact]
    public void Banking_a_cheque_before_its_date_is_refused()
    {
        // The whole meaning of a post-dated cheque: presented early it is returned,
        // and the firm pays a charge for the privilege.
        Cheque cheque = Received(1000m);

        Result result = cheque.Deposit(BankAccount, Matures.AddDays(-1));

        result.Error.Code.ShouldBe("Cheque.BankedBeforeItsDate");
        cheque.Status.ShouldBe(ChequeStatus.Pending);
    }

    [Fact]
    public void A_cheque_can_be_banked_on_the_day_it_bears()
    {
        Cheque cheque = Received(1000m);

        cheque.Deposit(BankAccount, Matures).IsSuccess.ShouldBeTrue();
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
        cheque.DepositedOn.ShouldBe(Matures);
    }

    [Fact]
    public void A_cheque_already_with_the_bank_cannot_be_banked_again()
    {
        Cheque cheque = Received(1000m);
        cheque.Deposit(BankAccount, Matures);

        cheque.Deposit(BankAccount, Matures.AddDays(1)).Error.Code.ShouldBe("Cheque.NotPending");
    }

    [Fact]
    public void An_issued_cheque_cannot_be_presented_against_another_account()
    {
        // It is drawn on one account and can be presented against no other.
        // Accepting a different one would put the payment through the wrong
        // reconciliation.
        Cheque cheque = Issued(1000m);

        cheque.Deposit(LedgerId.NewId(), Matures)
            .Error.Code.ShouldBe("Cheque.WrongDrawnOnAccount");
    }

    [Fact]
    public void An_issued_cheque_is_presented_against_the_account_it_was_drawn_on()
    {
        Cheque cheque = Issued(1000m);

        cheque.Deposit(BankAccount, Matures).IsSuccess.ShouldBeTrue();
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
    }

    // ------------------------------------------------------------ clearing

    [Fact]
    public void A_cleared_cheque_records_when_and_by_which_voucher()
    {
        Cheque cheque = Banked(1000m);
        VoucherId clearing = VoucherId.NewId();

        cheque.Clear(Matures.AddDays(3), clearing).IsSuccess.ShouldBeTrue();

        cheque.Status.ShouldBe(ChequeStatus.Cleared);
        cheque.ClearedOn.ShouldBe(Matures.AddDays(3));
        cheque.ClearingVoucherId.ShouldBe(clearing);
        cheque.IsClosed.ShouldBeTrue();
    }

    [Fact]
    public void A_cheque_still_in_hand_cannot_clear()
    {
        Cheque cheque = Received(1000m);

        cheque.Clear(Matures, VoucherId.NewId()).Error.Code.ShouldBe("Cheque.NotDeposited");
    }

    [Fact]
    public void A_cheque_cannot_have_cleared_before_it_was_banked()
    {
        Cheque cheque = Banked(1000m);

        cheque.Clear(Matures.AddDays(-1), VoucherId.NewId())
            .Error.Code.ShouldBe("Cheque.ClearedBeforeItWasBanked");
    }

    [Fact]
    public void Clearing_raises_an_event()
    {
        Cheque cheque = Banked(1000m);

        cheque.Clear(Matures, VoucherId.NewId());

        ChequeCleared cleared = cheque.DomainEvents.OfType<ChequeCleared>().ShouldHaveSingleItem();
        cleared.ChequeNumber.ShouldBe("000123");
        cleared.Amount.ShouldBe(Money.Of(1000m, Qar));
        cleared.ClearedOn.ShouldBe(Matures);
    }

    // ------------------------------------------------------------ bouncing

    [Fact]
    public void A_bounced_cheque_records_the_banks_reason()
    {
        // The reason decides what happens next: insufficient funds is re-presented,
        // a signature mismatch is replaced, a closed account is a collections
        // matter. Recording "bounced" alone loses that.
        Cheque cheque = Banked(1000m);

        cheque.Bounce("Insufficient funds", Matures.AddDays(2)).IsSuccess.ShouldBeTrue();

        cheque.Status.ShouldBe(ChequeStatus.Bounced);
        cheque.ClosureReason.ShouldBe("Insufficient funds");
        cheque.ClosedOn.ShouldBe(Matures.AddDays(2));
        cheque.ClearedOn.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_bounce_without_a_reason_is_refused(string reason)
    {
        Cheque cheque = Banked(1000m);

        cheque.Bounce(reason, Matures).Error.Code.ShouldBe("Cheque.BounceReasonRequired");
        cheque.Status.ShouldBe(ChequeStatus.Deposited);
    }

    [Fact]
    public void Bouncing_raises_an_event_carrying_the_reason()
    {
        // The consequential one: a receipt settled by this cheque has to be undone,
        // and whoever stopped chasing the debt needs telling.
        Cheque cheque = Banked(1000m);

        cheque.Bounce("Refer to drawer", Matures.AddDays(2));

        ChequeBounced bounced = cheque.DomainEvents.OfType<ChequeBounced>().ShouldHaveSingleItem();
        bounced.Reason.ShouldBe("Refer to drawer");
        bounced.PartyLedgerId.ShouldBe(Customer);
        bounced.Amount.ShouldBe(Money.Of(1000m, Qar));
    }

    [Fact]
    public void A_cleared_cheque_cannot_then_bounce()
    {
        Cheque cheque = Banked(1000m);
        cheque.Clear(Matures, VoucherId.NewId());

        cheque.Bounce("Too late", Matures.AddDays(5)).Error.Code.ShouldBe("Cheque.NotDeposited");
        cheque.Status.ShouldBe(ChequeStatus.Cleared);
    }

    [Fact]
    public void A_bounced_cheque_is_terminal()
    {
        // Re-presenting it is a new cheque, which is what the bank statement shows
        // too: the second presentation is a separate event with its own charges and
        // its own outcome.
        Cheque cheque = Banked(1000m);
        cheque.Bounce("Insufficient funds", Matures.AddDays(2));

        cheque.Clear(Matures.AddDays(9), VoucherId.NewId()).IsFailure.ShouldBeTrue();
        cheque.Deposit(BankAccount, Matures.AddDays(9)).IsFailure.ShouldBeTrue();
    }

    // ------------------------------------------------------------ stopping

    [Fact]
    public void Payment_can_be_stopped_on_a_cheque_the_firm_issued()
    {
        Cheque cheque = Issued(1000m);

        cheque.Stop("Goods never delivered", Matures.AddDays(-5)).IsSuccess.ShouldBeTrue();

        cheque.Status.ShouldBe(ChequeStatus.Stopped);
        cheque.ClosureReason.ShouldBe("Goods never delivered");
    }

    [Fact]
    public void An_issued_cheque_can_be_stopped_after_it_was_presented()
    {
        Cheque cheque = Issued(1000m);
        cheque.Deposit(BankAccount, Matures);

        cheque.Stop("Dispute raised", Matures).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_received_cheque_cannot_be_stopped_by_the_firm()
    {
        // Only the drawer can stop a cheque, and for one the firm received that is
        // the customer. From the firm's side it arrives as a bounce from the bank,
        // not an instruction the firm gave - and conflating the two would lose the
        // difference between a payment withdrawn and one reneged on.
        Cheque cheque = Received(1000m);

        cheque.Stop("Changed my mind", Matures)
            .Error.Code.ShouldBe("Cheque.OnlyIssuedCanBeStopped");
    }

    [Fact]
    public void A_cleared_cheque_cannot_be_stopped()
    {
        Cheque cheque = Issued(1000m);
        cheque.Deposit(BankAccount, Matures);
        cheque.Clear(Matures, VoucherId.NewId());

        cheque.Stop("Too late", Matures.AddDays(1)).Error.Code.ShouldBe("Cheque.AlreadyClosed");
    }

    // ------------------------------------------------------------ cancelling

    [Fact]
    public void A_cheque_in_hand_can_be_cancelled()
    {
        Cheque cheque = Received(1000m);

        cheque.Cancel("Replaced with a bank transfer", Taken.AddDays(1)).IsSuccess.ShouldBeTrue();

        cheque.Status.ShouldBe(ChequeStatus.Cancelled);
        cheque.IsOutstanding.ShouldBeFalse();
    }

    [Fact]
    public void A_cheque_with_the_bank_cannot_be_cancelled()
    {
        // Once it is in the banking system its outcome is the bank's to report, and
        // cancelling it here would leave the books saying nothing happened while the
        // statement says otherwise.
        Cheque cheque = Banked(1000m);

        cheque.Cancel("Changed our minds", Matures).Error.Code.ShouldBe("Cheque.NotPending");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_cancellation_without_a_reason_is_refused(string reason)
    {
        Received(1000m).Cancel(reason, Taken)
            .Error.Code.ShouldBe("Cheque.CancellationReasonRequired");
    }

    // ------------------------------------------------------------ helpers

    private static Result<Cheque> TryRecord(decimal amount, string number = "000123") =>
        Cheque.Record(
            Tenant, Firm, ChequeDirection.Received, Customer, VoucherId.NewId(),
            number, Matures, Taken, Money.Of(amount, Qar), drawnOnBank: "Qatar National Bank");

    private static Cheque Received(decimal amount) => TryRecord(amount).Value;

    private static Cheque Issued(decimal amount) =>
        Cheque.Record(
            Tenant, Firm, ChequeDirection.Issued, Customer, VoucherId.NewId(),
            "000123", Matures, Taken, Money.Of(amount, Qar), bankLedgerId: BankAccount).Value;

    /// <summary>A received cheque already paid in on the day it matured.</summary>
    private static Cheque Banked(decimal amount)
    {
        Cheque cheque = Received(amount);
        cheque.Deposit(BankAccount, Matures).IsSuccess.ShouldBeTrue();

        return cheque;
    }
}
