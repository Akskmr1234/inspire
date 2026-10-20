using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Tests.Service;

/// <summary>Tests for <see cref="JobCard"/>.</summary>
public sealed class JobCardTests
{
    private static readonly TenantId Tenant = TenantId.NewId();
    private static readonly FirmId Firm = FirmId.NewId();
    private static readonly DateOnly Date = new(2026, 8, 7);

    [Fact]
    public void The_status_is_the_furthest_stage_reached()
    {
        JobCard card = Card();

        card.Status.ShouldBe(JobCardStatus.Received);

        card.MarkStage(JobCardStage.Processing, Date).IsSuccess.ShouldBeTrue();
        card.Status.ShouldBe(JobCardStatus.Processing);

        card.MarkStage(JobCardStage.Ready, Date.AddDays(1)).IsSuccess.ShouldBeTrue();
        card.MarkStage(JobCardStage.Informed, Date.AddDays(1)).IsSuccess.ShouldBeTrue();
        card.Status.ShouldBe(JobCardStatus.Informed);

        card.MarkStage(JobCardStage.Delivered, Date.AddDays(2)).IsSuccess.ShouldBeTrue();
        card.Status.ShouldBe(JobCardStatus.Delivered);

        // Clearing a tick put on by mistake steps the job back.
        card.MarkStage(JobCardStage.Delivered, null).IsSuccess.ShouldBeTrue();
        card.Status.ShouldBe(JobCardStatus.Informed);
        card.DeliveredOn.ShouldBeNull();
    }

    [Fact]
    public void A_billed_job_keeps_its_parts_and_cannot_be_withdrawn()
    {
        JobCard card = Card();
        Product part = Part();

        card.AddLine(part, 2m, 30m).IsSuccess.ShouldBeTrue();
        card.RecordInvoice(SalesInvoiceId.NewId()).IsSuccess.ShouldBeTrue();

        card.IsBillable.ShouldBeFalse();
        card.AddLine(part, 1m, 30m).Error.Code.ShouldBe("JobCard.Locked");
        card.ClearLines().Error.Code.ShouldBe("JobCard.Locked");
        card.Cancel("Customer left").Error.Code.ShouldBe("JobCard.Billed");

        // Its invoice cancelled, the job is open again.
        card.ReleaseInvoice();
        card.Cancel("Customer left").IsSuccess.ShouldBeTrue();
        card.Status.ShouldBe(JobCardStatus.Cancelled);
        card.MarkStage(JobCardStage.Ready, Date).Error.Code.ShouldBe("JobCard.Cancelled");
    }

    [Fact]
    public void A_job_with_nothing_on_it_cannot_be_billed_and_a_line_needs_a_quantity()
    {
        JobCard card = Card();

        card.RecordInvoice(SalesInvoiceId.NewId()).Error.Code.ShouldBe("JobCard.NothingToBill");
        card.AddLine(Part(), 0m, 30m).Error.Code.ShouldBe("JobCard.LineInvalid");
        card.AddLine(Part(), 1m, -1m).Error.Code.ShouldBe("JobCard.LineInvalid");
    }

    [Fact]
    public void The_customer_and_the_device_are_required()
    {
        JobCard card = Card();

        card.SetCustomer(new JobCardCustomer(" "), null, null, SalesPaymentMode.Cash)
            .Error.Code.ShouldBe("JobCard.CustomerRequired");
        card.SetDevice(new JobCardDevice(string.Empty)).Error.Code.ShouldBe("JobCard.ItemRequired");
        card.SetWork(new JobCardWork(EstimatedCharge: -1m), null, null)
            .Error.Code.ShouldBe("JobCard.EstimateNegative");

        card.SetCustomer(new JobCardCustomer("  Ahmed  ", "0551234567"), null, "REF 1", SalesPaymentMode.Credit)
            .IsSuccess.ShouldBeTrue();
        card.CustomerName.ShouldBe("Ahmed");
        card.PaymentMode.ShouldBe(SalesPaymentMode.Credit);
    }

    private static JobCard Card()
    {
        FinancialYear year = FinancialYear.Create(
            Tenant, Firm, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), []).Value;
        Warehouse godown = Warehouse.Create(Tenant, Firm, "MAIN", "Main godown").Value;

        return JobCard.Create(Tenant, Firm, year, "JC/2026/0001", Date, godown).Value;
    }

    private static Product Part() =>
        Product.Create(
            Category.CreateRoot(Tenant, Firm, "GEN", "General").Value,
            UnitOfMeasure.CreateBase(Tenant, Firm, "EACH", "Each").Value,
            $"PRO-{Guid.NewGuid():N}"[..12],
            "A display",
            ItemType.Stock,
            CurrencyCode.Qar).Value;
}
