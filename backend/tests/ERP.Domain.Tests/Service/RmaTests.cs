using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Service;

/// <summary>Tests for <see cref="Rma"/>.</summary>
public sealed class RmaTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly DateOnly Date = new(2026, 8, 7);
    private static readonly RmaActor Clerk = new(UserId.NewId(), "Clerk", new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_return_goes_through_approval_and_inspection_to_its_outcome_and_keeps_its_history()
    {
        Rma rma = Request(RmaDirection.FromCustomer, LedgerKind.Customer);

        rma.Receive(Clerk, null).Error.Code.ShouldBe("Rma.WrongStatus");
        rma.Approve(Clerk, "Under warranty").IsSuccess.ShouldBeTrue();

        // What is going back is settled at approval.
        rma.AddLine(Part(), 1m).Error.Code.ShouldBe("Rma.Locked");

        rma.Receive(Clerk, null).IsSuccess.ShouldBeTrue();
        rma.Inspect(RmaCondition.Repairable, "Loose connector", Date, Clerk).IsSuccess.ShouldBeTrue();
        rma.Repair(Clerk, "Connector resoldered").IsSuccess.ShouldBeTrue();
        rma.Close(Clerk, null).IsSuccess.ShouldBeTrue();

        rma.IsFinished.ShouldBeTrue();
        rma.Condition.ShouldBe(RmaCondition.Repairable);
        rma.Events.Select(entry => entry.Status).ShouldBe(
        [
            RmaStatus.Requested, RmaStatus.Approved, RmaStatus.Received,
            RmaStatus.UnderInspection, RmaStatus.Repaired, RmaStatus.Closed,
        ]);
        rma.Events[1].Note.ShouldBe("Under warranty");
        rma.Events.ShouldAllBe(entry => entry.UserName == "Clerk");
    }

    [Fact]
    public void Nothing_is_approved_without_products_and_a_refusal_needs_a_reason()
    {
        Ledger customer = Party(LedgerKind.Customer);
        Rma rma = Rma.Create(
            Tenant, Firm, Year(), "RMA/2026/0002", Date, RmaDirection.FromCustomer, customer, Godown(), Clerk).Value;

        rma.Approve(Clerk, null).Error.Code.ShouldBe("Rma.NothingToReturn");
        rma.Reject(Clerk, " ").Error.Code.ShouldBe("Rma.ReasonRequired");
        rma.Reject(Clerk, "Out of warranty").IsSuccess.ShouldBeTrue();
        rma.Approve(Clerk, null).Error.Code.ShouldBe("Rma.WrongStatus");
    }

    [Fact]
    public void The_party_must_match_the_direction_and_a_refund_the_party()
    {
        Rma.Create(
            Tenant, Firm, Year(), "RMA/2026/0003", Date, RmaDirection.ToSupplier, Party(LedgerKind.Customer),
            Godown(), Clerk).Error.Code.ShouldBe("Rma.WrongParty");

        Rma rma = Request(RmaDirection.ToSupplier, LedgerKind.Supplier);
        rma.Approve(Clerk, null).IsSuccess.ShouldBeTrue();
        rma.Receive(Clerk, null).IsSuccess.ShouldBeTrue();

        // A supplier is charged back with a debit note, not refunded with a credit note.
        rma.Refund(SalesInvoiceId.NewId(), Clerk, null).Error.Code.ShouldBe("Rma.WrongStatus");
        rma.Refund(PurchaseInvoiceId.NewId(), Clerk, null).IsSuccess.ShouldBeTrue();

        rma.ReopenRefund(Clerk).IsSuccess.ShouldBeTrue();
        rma.Status.ShouldBe(RmaStatus.UnderInspection);
        rma.DebitNoteId.ShouldBeNull();
    }

    [Fact]
    public void Photographs_are_pictures_and_limited_in_number()
    {
        Rma rma = Request(RmaDirection.FromCustomer, LedgerKind.Customer);

        rma.AddPhoto("hello", null).Error.Code.ShouldBe("Rma.PhotoInvalid");

        for (int index = 0; index < Rma.MaximumPhotos; index++)
        {
            rma.AddPhoto("data:image/png;base64,AAAA", $"Photo {index}").IsSuccess.ShouldBeTrue();
        }

        rma.AddPhoto("data:image/png;base64,AAAA", null).Error.Code.ShouldBe("Rma.TooManyPhotos");
        rma.RemovePhoto(rma.Photos[0].Id).IsSuccess.ShouldBeTrue();
        rma.Photos.Count.ShouldBe(Rma.MaximumPhotos - 1);
    }

    private static Rma Request(RmaDirection direction, LedgerKind kind)
    {
        Rma rma = Rma.Create(
            Tenant, Firm, Year(), "RMA/2026/0001", Date, direction, Party(kind), Godown(), Clerk).Value;

        rma.Describe("Does not switch on", null, "INV-1", null, null).IsSuccess.ShouldBeTrue();
        rma.AddLine(Part(), 1m, serialNumber: "SN-1").IsSuccess.ShouldBeTrue();

        return rma;
    }

    private static FinancialYear Year() =>
        FinancialYear.Create(Tenant, Firm, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;

    private static Warehouse Godown() => Warehouse.Create(Tenant, Firm, "MAIN", "Main godown").Value;

    private static Ledger Party(LedgerKind kind) =>
        Ledger.Create(
            AccountGroup.CreateRoot(Tenant, Firm, "G1", "Group", AccountNature.Asset).Value,
            "P1",
            "Party",
            kind,
            CurrencyCode.Qar).Value;

    private static Product Part() =>
        Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value,
            $"PRO-{Guid.NewGuid():N}"[..12],
            "A handset",
            ItemType.Stock,
            CurrencyCode.Qar).Value;
}
