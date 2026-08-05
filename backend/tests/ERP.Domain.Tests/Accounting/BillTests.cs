using ERP.Domain.Accounting;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Accounting;

/// <summary>
/// Tests for <see cref="Bill"/>.
/// </summary>
/// <remarks>
/// Bill-wise settlement is what makes "which invoices are still unpaid"
/// answerable. A running balance can say a customer owes 12,000; only the bills
/// can say whether that is one overdue invoice from March or six current ones.
/// Every figure on the Outstanding and Aging reports comes from this arithmetic,
/// so the invariants below are the ones those reports rest on.
/// </remarks>
public sealed class BillTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly LedgerId Customer = LedgerId.NewId();
    private static readonly DateOnly Raised = new(2026, 4, 1);
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;

    // ------------------------------------------------------------ raising

    [Fact]
    public void A_new_bill_is_open_and_wholly_outstanding()
    {
        Bill bill = Raise(1000m);

        bill.Status.ShouldBe(BillStatus.Open);
        bill.OutstandingAmount.ShouldBe(Money.Of(1000m, Qar));
        bill.SettledAmount.IsZero.ShouldBeTrue();
        bill.Allocations.ShouldBeEmpty();
    }

    [Fact]
    public void The_due_date_comes_from_the_credit_terms()
    {
        Raise(1000m, creditDays: 30).DueDate.ShouldBe(new DateOnly(2026, 5, 1));
        Raise(1000m, creditDays: 0).DueDate.ShouldBe(Raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_bill_must_be_raised_for_a_positive_amount(decimal amount)
    {
        // A credit note is a bill of the opposite type, not a negative one -
        // otherwise every outstanding total would silently net off against itself.
        Result<Bill> result = TryRaise(amount);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Bill.AmountNotPositive");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_bill_reference_is_required(string number) =>
        TryRaise(1000m, number: number).Error.Code.ShouldBe("Bill.NumberRequired");

    [Fact]
    public void Negative_credit_days_are_rejected() =>
        TryRaise(1000m, creditDays: -1).Error.Code.ShouldBe("Bill.CreditDaysNegative");

    // ------------------------------------------------------------ allocation

    [Fact]
    public void A_part_payment_leaves_the_remainder_outstanding()
    {
        Bill bill = Raise(1000m);

        bill.Allocate(VoucherId.NewId(), Money.Of(400m, Qar), Raised.AddDays(10))
            .IsSuccess.ShouldBeTrue();

        bill.Status.ShouldBe(BillStatus.PartiallySettled);
        bill.SettledAmount.ShouldBe(Money.Of(400m, Qar));
        bill.OutstandingAmount.ShouldBe(Money.Of(600m, Qar));
    }

    [Fact]
    public void Several_receipts_can_settle_one_bill()
    {
        // The common case: an invoice paid down over months, each receipt its own
        // voucher, possibly in different financial years.
        Bill bill = Raise(1000m);

        bill.Allocate(VoucherId.NewId(), Money.Of(300m, Qar), Raised.AddDays(10));
        bill.Allocate(VoucherId.NewId(), Money.Of(300m, Qar), Raised.AddDays(40));
        bill.Allocate(VoucherId.NewId(), Money.Of(400m, Qar), Raised.AddDays(80));

        bill.Status.ShouldBe(BillStatus.Settled);
        bill.OutstandingAmount.IsZero.ShouldBeTrue();
        bill.Allocations.Count.ShouldBe(3);
    }

    [Fact]
    public void Settling_exactly_closes_the_bill()
    {
        Bill bill = Raise(1000m);

        bill.Allocate(VoucherId.NewId(), Money.Of(1000m, Qar), Raised.AddDays(5));

        bill.Status.ShouldBe(BillStatus.Settled);
        bill.OutstandingAmount.IsZero.ShouldBeTrue();
    }

    [Fact]
    public void Over_allocation_is_refused_rather_than_silently_capped()
    {
        // A receipt exceeding what the bill is owed means the wrong bill was
        // picked or the wrong figure typed. Absorbing the difference would hide
        // the mistake and leave the party's balance wrong.
        Bill bill = Raise(1000m);

        Result result = bill.Allocate(VoucherId.NewId(), Money.Of(1000.01m, Qar), Raised);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Bill.OverAllocated");
        result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);

        // And nothing changed.
        bill.SettledAmount.IsZero.ShouldBeTrue();
        bill.Status.ShouldBe(BillStatus.Open);
    }

    [Fact]
    public void Allocating_more_than_the_remainder_is_refused()
    {
        Bill bill = Raise(1000m);
        bill.Allocate(VoucherId.NewId(), Money.Of(700m, Qar), Raised);

        bill.Allocate(VoucherId.NewId(), Money.Of(301m, Qar), Raised)
            .Error.Code.ShouldBe("Bill.OverAllocated");

        bill.OutstandingAmount.ShouldBe(Money.Of(300m, Qar));
    }

    [Fact]
    public void A_settled_bill_takes_no_further_allocations()
    {
        Bill bill = Raise(500m);
        bill.Allocate(VoucherId.NewId(), Money.Of(500m, Qar), Raised);

        bill.Allocate(VoucherId.NewId(), Money.Of(1m, Qar), Raised)
            .Error.Code.ShouldBe("Bill.AlreadySettled");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void An_allocation_must_be_positive(decimal amount)
    {
        Raise(1000m).Allocate(VoucherId.NewId(), Money.Of(amount, Qar), Raised)
            .Error.Code.ShouldBe("Bill.AllocationNotPositive");
    }

    [Fact]
    public void An_allocation_in_another_currency_is_refused()
    {
        // Converting is the caller's job, and doing it implicitly here would bury
        // the exchange rate used inside a settlement nobody can later audit.
        Bill bill = Raise(1000m);

        bill.Allocate(VoucherId.NewId(), Money.Of(1000m, CurrencyCode.Inr), Raised)
            .Error.Code.ShouldBe("Bill.CurrencyMismatch");
    }

    // ------------------------------------------------------------ reversal

    [Fact]
    public void Cancelling_a_receipt_releases_what_it_settled()
    {
        // Without this a cancelled receipt would leave its bills showing as paid,
        // and the customer's outstanding would understate what they owe - found
        // only when somebody chases a payment that was never really made.
        Bill bill = Raise(1000m);
        VoucherId cancelled = VoucherId.NewId();

        bill.Allocate(VoucherId.NewId(), Money.Of(200m, Qar), Raised);
        bill.Allocate(cancelled, Money.Of(300m, Qar), Raised);

        Money released = bill.ReleaseAllocationsFrom(cancelled);

        released.ShouldBe(Money.Of(300m, Qar));
        bill.SettledAmount.ShouldBe(Money.Of(200m, Qar));
        bill.OutstandingAmount.ShouldBe(Money.Of(800m, Qar));
        bill.Status.ShouldBe(BillStatus.PartiallySettled);
        bill.Allocations.Count.ShouldBe(1);
    }

    [Fact]
    public void Releasing_reopens_a_settled_bill()
    {
        Bill bill = Raise(500m);
        VoucherId receipt = VoucherId.NewId();
        bill.Allocate(receipt, Money.Of(500m, Qar), Raised);

        bill.Status.ShouldBe(BillStatus.Settled);

        bill.ReleaseAllocationsFrom(receipt);

        bill.Status.ShouldBe(BillStatus.Open);
        bill.OutstandingAmount.ShouldBe(Money.Of(500m, Qar));

        // And it can be settled again afterwards.
        bill.Allocate(VoucherId.NewId(), Money.Of(500m, Qar), Raised).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Releasing_a_voucher_that_settled_nothing_changes_nothing()
    {
        Bill bill = Raise(1000m);
        bill.Allocate(VoucherId.NewId(), Money.Of(400m, Qar), Raised);

        Money released = bill.ReleaseAllocationsFrom(VoucherId.NewId());

        released.IsZero.ShouldBeTrue();
        bill.SettledAmount.ShouldBe(Money.Of(400m, Qar));
    }

    [Fact]
    public void All_allocations_from_one_voucher_are_released_together()
    {
        // A single receipt may settle one bill in two lines. Releasing must take
        // both, or the remainder would stay wrongly settled.
        Bill bill = Raise(1000m);
        VoucherId receipt = VoucherId.NewId();

        bill.Allocate(receipt, Money.Of(300m, Qar), Raised);
        bill.Allocate(receipt, Money.Of(200m, Qar), Raised);

        bill.ReleaseAllocationsFrom(receipt).ShouldBe(Money.Of(500m, Qar));
        bill.OutstandingAmount.ShouldBe(Money.Of(1000m, Qar));
        bill.Allocations.ShouldBeEmpty();
    }

    // ------------------------------------------------------------ aging

    [Fact]
    public void Aging_counts_from_the_due_date_not_the_bill_date()
    {
        // Aging from when the invoice was raised would report every bill as
        // overdue the moment it is issued, which makes the report useless.
        Bill bill = Raise(1000m, creditDays: 30);

        bill.DaysOverdueAt(Raised).ShouldBe(0);
        bill.DaysOverdueAt(bill.DueDate).ShouldBe(0);
        bill.DaysOverdueAt(bill.DueDate.AddDays(1)).ShouldBe(1);
        bill.DaysOverdueAt(bill.DueDate.AddDays(45)).ShouldBe(45);
    }

    [Fact]
    public void A_bill_is_not_overdue_before_its_due_date()
    {
        Bill bill = Raise(1000m, creditDays: 30);

        bill.IsOverdueAt(bill.DueDate).ShouldBeFalse();
        bill.IsOverdueAt(bill.DueDate.AddDays(1)).ShouldBeTrue();
    }

    [Fact]
    public void A_settled_bill_is_never_overdue()
    {
        // However late it was paid, it is no longer a debt - so it must not keep
        // appearing on a chase list.
        Bill bill = Raise(1000m, creditDays: 30);
        bill.Allocate(VoucherId.NewId(), Money.Of(1000m, Qar), Raised.AddDays(200));

        bill.IsOverdueAt(Raised.AddDays(365)).ShouldBeFalse();
    }

    [Fact]
    public void A_settled_bill_raises_an_event_once_it_closes()
    {
        // Consumed by notifications, so a credit controller stops chasing a debt
        // the moment it is paid.
        Bill bill = Raise(1000m);

        bill.Allocate(VoucherId.NewId(), Money.Of(600m, Qar), Raised);
        bill.DomainEvents.OfType<BillSettled>().ShouldBeEmpty();

        bill.Allocate(VoucherId.NewId(), Money.Of(400m, Qar), Raised.AddDays(5));

        BillSettled settled = bill.DomainEvents.OfType<BillSettled>().ShouldHaveSingleItem();
        settled.BillNumber.ShouldBe("INV-001");
        settled.SettledOn.ShouldBe(Raised.AddDays(5));
    }

    // ------------------------------------------------------------ helpers

    private static Result<Bill> TryRaise(
        decimal amount,
        string number = "INV-001",
        int creditDays = 30) =>
        Bill.Raise(
            Tenant, Firm, Customer, VoucherId.NewId(), BillType.Receivable,
            number, Raised, creditDays, Money.Of(amount, Qar));

    private static Bill Raise(decimal amount, int creditDays = 30) =>
        TryRaise(amount, creditDays: creditDays).Value;
}
