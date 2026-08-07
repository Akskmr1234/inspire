using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>Tests for <see cref="StockBalance"/>: weighted average costing.</summary>
/// <remarks>
/// Open question 6 was answered <em>average costing, FIFO is not required</em>, and
/// this is where that answer becomes arithmetic. What these cover is the arithmetic
/// itself, the precision it is kept to, and the three cases that quietly produce a
/// wrong valuation everywhere else: the first receipt into an empty position, the
/// position that reaches zero, and an issue for more than is on hand.
/// </remarks>
public sealed class StockBalanceTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateTimeOffset Now =
        new(2026, 8, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_position_holds_nothing_and_costs_nothing()
    {
        StockBalance balance = Open();

        balance.Quantity.ShouldBe(0m);
        balance.AverageCost.ShouldBe(0m);
        balance.Value.Amount.ShouldBe(0m);
        balance.LastMovementAtUtc.ShouldBeNull();
    }

    [Fact]
    public void The_first_receipt_sets_the_average_to_what_it_cost()
    {
        StockBalance balance = Open();

        balance.Receive(10m, 25m, Now).Value.Amount.ShouldBe(250m);

        balance.Quantity.ShouldBe(10m);
        balance.AverageCost.ShouldBe(25m);
        balance.Value.Amount.ShouldBe(250m);
    }

    [Fact]
    public void A_second_receipt_moves_the_average_towards_it_by_weight()
    {
        // The textbook case: 10 at 25 and 30 at 35 is 1150 over 40, not 30. An average
        // of the two prices rather than of the value is the single most common way an
        // inventory valuation is wrong.
        StockBalance balance = Open();

        balance.Receive(10m, 25m, Now);
        balance.Receive(30m, 35m, Now);

        balance.Quantity.ShouldBe(40m);
        balance.AverageCost.ShouldBe(32.5m);
        balance.Value.Amount.ShouldBe(1300m);
    }

    [Fact]
    public void An_issue_leaves_the_average_where_it_was()
    {
        StockBalance balance = Open();
        balance.Receive(40m, 32.5m, Now);

        balance.Issue(15m, Now).Value.Amount.ShouldBe(487.5m);

        balance.Quantity.ShouldBe(25m);
        balance.AverageCost.ShouldBe(32.5m);
        balance.Value.Amount.ShouldBe(812.5m);
    }

    [Fact]
    public void An_issue_for_more_than_is_on_hand_is_refused()
    {
        // Negative stock is a real request and a defensible refusal: there is no cost
        // for goods the system does not believe exist, so permitting it would produce
        // a valuation nobody can stand behind.
        StockBalance balance = Open();
        balance.Receive(5m, 10m, Now);

        balance.Issue(6m, Now).Error.Code.ShouldBe("StockBalance.Insufficient");

        balance.Quantity.ShouldBe(5m);
    }

    [Fact]
    public void A_position_emptied_keeps_the_cost_it_reached()
    {
        // A product that sells out did not stop having a history. Zeroing the average
        // would make a report run between the last sale and the next purchase say the
        // goods cost nothing.
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);
        balance.Issue(10m, Now);

        balance.Quantity.ShouldBe(0m);
        balance.AverageCost.ShouldBe(25m);
        balance.Value.Amount.ShouldBe(0m);
    }

    [Fact]
    public void A_receipt_into_an_emptied_position_starts_the_average_again()
    {
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);
        balance.Issue(10m, Now);

        balance.Receive(4m, 40m, Now);

        // Not an average of 25 and 40: there was nothing left of the first lot to
        // average against.
        balance.AverageCost.ShouldBe(40m);
        balance.Value.Amount.ShouldBe(160m);
    }

    [Fact]
    public void The_average_is_kept_to_six_places_rather_than_the_currencys_two()
    {
        // 100 at 3.33 and 1 at 10 is 343 over 101 = 3.396039...  Rounded to the
        // currency at every receipt, the error would land in the valuation in the same
        // direction for as long as the product exists.
        StockBalance balance = Open();

        balance.Receive(100m, 3.33m, Now);
        balance.Receive(1m, 10m, Now);

        balance.AverageCost.ShouldBe(3.39604m, 0.000005m);
        balance.Value.Amount.ShouldBe(343m, 0.01m);
    }

    [Fact]
    public void The_order_receipts_arrive_in_does_not_change_the_position()
    {
        // The property that makes average costing safe to answer question 6 with:
        // there is no queue, so nothing later can consume the wrong lot.
        StockBalance forwards = Open();
        forwards.Receive(10m, 25m, Now);
        forwards.Receive(30m, 35m, Now);

        StockBalance backwards = Open();
        backwards.Receive(30m, 35m, Now);
        backwards.Receive(10m, 25m, Now);

        backwards.Quantity.ShouldBe(forwards.Quantity);
        backwards.AverageCost.ShouldBe(forwards.AverageCost);
    }

    [Fact]
    public void A_receipt_of_nothing_or_at_a_negative_cost_is_refused()
    {
        StockBalance balance = Open();

        balance.Receive(0m, 10m, Now).Error.Code
            .ShouldBe("StockBalance.QuantityNotPositive");
        balance.Receive(-1m, 10m, Now).Error.Code
            .ShouldBe("StockBalance.QuantityNotPositive");
        balance.Receive(1m, -10m, Now).Error.Code.ShouldBe("StockBalance.CostNegative");
    }

    [Fact]
    public void An_issue_of_nothing_is_refused()
    {
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);

        balance.Issue(0m, Now).Error.Code.ShouldBe("StockBalance.QuantityNotPositive");
        balance.Issue(-5m, Now).Error.Code.ShouldBe("StockBalance.QuantityNotPositive");
    }

    [Fact]
    public void Reversing_a_receipt_removes_exactly_what_it_added()
    {
        // The case that makes this a separate operation from an issue: the average has
        // moved since. Issuing ten would take 300 out where 250 went in, and the 50
        // would vanish into the average of what is left with nothing to show for it.
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);
        balance.Receive(10m, 35m, Now);
        balance.AverageCost.ShouldBe(30m);

        balance.ReverseReceipt(10m, 25m, Now).Value.Amount.ShouldBe(250m);

        balance.Quantity.ShouldBe(10m);
        balance.AverageCost.ShouldBe(35m);
        balance.Value.Amount.ShouldBe(350m);
    }

    [Fact]
    public void A_receipt_already_sold_on_cannot_be_reversed()
    {
        // Un-receiving goods that have left is not something the books can express,
        // and inventing a number for it would be worse than saying so.
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);
        balance.Issue(6m, Now);

        balance.ReverseReceipt(10m, 25m, Now).Error.Code
            .ShouldBe("StockBalance.ReceiptConsumed");

        balance.Quantity.ShouldBe(4m);
    }

    [Fact]
    public void A_reversal_that_would_leave_a_negative_value_is_refused()
    {
        // Ten received at 40 into a position averaging 10 cannot be unpicked: the 400
        // it claims to remove is more value than the position holds.
        StockBalance balance = Open();
        balance.Receive(100m, 7m, Now);
        balance.Issue(90m, Now);

        balance.ReverseReceipt(10m, 40m, Now).Error.Code
            .ShouldBe("StockBalance.ReversalBelowZero");
    }

    [Fact]
    public void A_revaluation_restates_the_cost_and_reports_the_difference()
    {
        // A write-down, not a receipt: no goods arrived, so the quantity is untouched
        // and the whole change in value is the loss.
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);

        balance.Revalue(18m, Now).Value.Amount.ShouldBe(-70m);

        balance.Quantity.ShouldBe(10m);
        balance.AverageCost.ShouldBe(18m);
        balance.Value.Amount.ShouldBe(180m);
    }

    [Fact]
    public void A_revaluation_to_a_negative_cost_is_refused()
    {
        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);

        balance.Revalue(-1m, Now).Error.Code.ShouldBe("StockBalance.CostNegative");
        balance.AverageCost.ShouldBe(25m);
    }

    [Fact]
    public void Every_movement_records_when_it_happened()
    {
        DateTimeOffset later = Now.AddDays(3);

        StockBalance balance = Open();
        balance.Receive(10m, 25m, Now);
        balance.LastMovementAtUtc.ShouldBe(Now);

        balance.Issue(1m, later);
        balance.LastMovementAtUtc.ShouldBe(later);
    }

    private static StockBalance Open() =>
        StockBalance.Open(Tenant, Firm, ProductId.NewId(), WarehouseId.NewId(), Qar);
}
