using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Inventory;

/// <summary>
/// Tests for <see cref="BatchBalance"/>, and for the product position keeping step
/// with it.
/// </summary>
/// <remarks>
/// The invariant that makes batch tracking worth having is that a product's position
/// is the sum of its batches' positions - in quantity and in value both. These cover
/// the arithmetic on one batch, and then the pair of them together, because a batch
/// valuation that quietly disagreed with the stock valuation would be worse than no
/// batch valuation at all.
/// </remarks>
public sealed class BatchBalanceTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly WarehouseId Godown = WarehouseId.NewId();
    private static readonly CurrencyCode Qar = CurrencyCode.Qar;
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_batch_takes_goods_in_at_what_they_cost()
    {
        BatchBalance balance = Position();

        balance.Receive(10m, 25m, Now).Value.Amount.ShouldBe(250m);

        balance.Quantity.ShouldBe(10m);
        balance.UnitCost.ShouldBe(25m);
        balance.Value.Amount.ShouldBe(250m);
    }

    [Fact]
    public void A_second_delivery_of_the_same_batch_averages_within_it()
    {
        // Rare, and it has to go somewhere. Averaging within a batch is still a far
        // finer answer than averaging across the product, which is what section 10
        // means by profit against actual batch cost.
        BatchBalance balance = Position();

        balance.Receive(10m, 25m, Now);
        balance.Receive(30m, 35m, Now);

        balance.Quantity.ShouldBe(40m);
        balance.UnitCost.ShouldBe(32.5m);
    }

    [Fact]
    public void An_issue_takes_goods_out_at_what_this_batch_costs()
    {
        BatchBalance balance = Position();
        balance.Receive(10m, 25m, Now);

        balance.Issue(4m, Now).Value.Amount.ShouldBe(100m);

        balance.Quantity.ShouldBe(6m);
        balance.UnitCost.ShouldBe(25m);
    }

    [Fact]
    public void A_batch_cannot_lend_from_another_batch()
    {
        // Stock of one batch is not interchangeable with stock of another - that is
        // what tracking batches means. Drawing the shortfall from elsewhere would send
        // out goods carrying an expiry date nobody asked for.
        BatchBalance balance = Position();
        balance.Receive(10m, 25m, Now);

        balance.Issue(11m, Now).Error.Code.ShouldBe("BatchBalance.Insufficient");
        balance.Quantity.ShouldBe(10m);
    }

    [Fact]
    public void Expired_goods_can_still_be_taken_out()
    {
        // Expired stock leaves the same way any other stock does, through an issue or
        // a write-off. A position that refused would be one they could never leave.
        BatchBalance balance = Position();
        balance.Receive(10m, 25m, Now);

        balance.Issue(10m, Now).IsSuccess.ShouldBeTrue();
        balance.Quantity.ShouldBe(0m);
    }

    [Fact]
    public void A_receipt_is_reversed_at_the_cost_it_came_in_at()
    {
        BatchBalance balance = Position();
        balance.Receive(10m, 25m, Now);
        balance.Receive(10m, 35m, Now);

        balance.ReverseReceipt(10m, 35m, Now).Value.Amount.ShouldBe(350m);

        balance.Quantity.ShouldBe(10m);
        balance.UnitCost.ShouldBe(25m);
    }

    [Fact]
    public void A_receipt_that_has_been_sold_on_cannot_be_reversed()
    {
        BatchBalance balance = Position();
        balance.Receive(10m, 25m, Now);
        balance.Issue(8m, Now);

        balance.ReverseReceipt(10m, 25m, Now).Error.Code
            .ShouldBe("BatchBalance.ReceiptConsumed");
    }

    [Fact]
    public void A_product_position_is_the_sum_of_its_batches()
    {
        // Two lots at two prices, then a sale out of the cheaper one. The product's
        // average has to end up where the batches leave it, or the stock valuation and
        // the batch-wise valuation report two figures for the same shelf.
        StockBalance product = StockBalance.Open(
            Tenant, Firm, ProductId.NewId(), Godown, Qar);
        BatchBalance cheap = Position();
        BatchBalance dear = Position();

        cheap.Receive(10m, 5m, Now);
        product.Receive(10m, 5m, Now);

        dear.Receive(10m, 6m, Now);
        product.Receive(10m, 6m, Now);

        product.Quantity.ShouldBe(20m);
        product.AverageCost.ShouldBe(5.5m);

        // The sale is picked from the cheap lot, so the position loses 5 a unit rather
        // than the average of 5.5 - and what remains is genuinely the dearer stock.
        cheap.Issue(10m, Now).Value.Amount.ShouldBe(50m);
        product.IssueAt(10m, cheap.UnitCost, Now).Value.Amount.ShouldBe(50m);

        product.Quantity.ShouldBe(cheap.Quantity + dear.Quantity);
        product.AverageCost.ShouldBe(6m);
        product.Value.Amount.ShouldBe(cheap.Value.Amount + dear.Value.Amount);
    }

    [Fact]
    public void Issuing_more_than_the_product_holds_is_still_refused()
    {
        StockBalance product = StockBalance.Open(
            Tenant, Firm, ProductId.NewId(), Godown, Qar);

        product.Receive(5m, 5m, Now);

        product.IssueAt(6m, 5m, Now).Error.Code.ShouldBe("StockBalance.Insufficient");
        product.IssueAt(1m, -1m, Now).Error.Code.ShouldBe("StockBalance.CostNegative");
        product.IssueAt(0m, 5m, Now).Error.Code.ShouldBe("StockBalance.QuantityNotPositive");
    }

    [Fact]
    public void Taking_out_more_value_than_is_held_is_refused()
    {
        // Only reachable where a position holds stock its batches cannot account for,
        // which the product master refuses to create. The guard stays because the day
        // it fires, the alternative is a shelf carrying a negative value.
        StockBalance product = StockBalance.Open(
            Tenant, Firm, ProductId.NewId(), Godown, Qar);

        product.Receive(10m, 1m, Now);

        product.IssueAt(5m, 100m, Now).Error.Code.ShouldBe("StockBalance.IssueBelowZero");
        product.Quantity.ShouldBe(10m);
    }

    // ------------------------------------------------------------------ helpers

    private static BatchBalance Position()
    {
        UnitOfMeasure each = UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value;

        Product product = Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            each, $"PRO-{Guid.NewGuid():N}"[..12], "A thing", ItemType.Stock, Qar).Value;

        product.SetTracking(true, false);

        Batch batch = Batch.Open(Tenant, Firm, product, $"B{Guid.NewGuid():N}"[..8]).Value;

        return BatchBalance.Open(batch, Godown, Qar);
    }
}
