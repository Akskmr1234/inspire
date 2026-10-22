using System.Net;
using System.Net.Http.Json;
using ERP.Application.Abstractions;
using ERP.Application.Accounting.Ledgers;
using ERP.Application.Inventory.Stock;
using ERP.Application.Sales;
using ERP.Application.Service;
using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Service;

namespace ERP.Api.Tests;

/// <summary>
/// Service job cards: a device taken in, its stages ticked off, its parts and labour billed
/// through an ordinary invoice, and the customer found again by their mobile number.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class JobCardEndpointTests
{
    private const string Cards = "/api/v1/service/job-cards";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="JobCardEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public JobCardEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_job_is_taken_in_worked_billed_and_delivered()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string mobile = $"05{Random.Shared.Next(10_000_000, 99_999_999)}";
        Guid technician = await EmployeeAsync(client);
        await ReceiveAsync(client, masters, 10m);

        // A customer is required, and so is the device.
        (await client.PostAsJsonAsync(Cards, Card(masters, mobile, technician, name: string.Empty)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        HttpResponseMessage created = await client.PostAsJsonAsync(Cards, Card(masters, mobile, technician));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        JobCardDetail card = (await client.GetFromJsonAsync<JobCardDetail>($"{Cards}/{id}"))!;
        card.Summary.Number.ShouldStartWith("JC");
        card.Summary.Status.ShouldBe(JobCardStatus.Received);
        card.Summary.TechnicianName.ShouldNotBeNull();
        card.Device.SerialNumber.ShouldBe("IMEI-1");
        card.Lines.Count.ShouldBe(1);

        // The diagnosis and the part used are added as the work goes on.
        (await client.PutAsJsonAsync($"{Cards}/{id}", Card(
            masters, mobile, technician, problem: "Cracked display", quantity: 2m)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync($"{Cards}/{id}/stage", new { Stage = JobCardStage.Processing, On = Today }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"{Cards}/{id}/stage", new { Stage = JobCardStage.Ready, On = Today }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        card = (await client.GetFromJsonAsync<JobCardDetail>($"{Cards}/{id}"))!;
        card.Summary.Status.ShouldBe(JobCardStatus.Ready);
        card.Work.Problem.ShouldBe("Cracked display");
        card.Summary.Amount.ShouldBe(60m);
        card.Stages.Processing.ShouldBe(Today);

        // Billed as a draft sales invoice of the parts and labour; the card leaves the
        // stock alone until the invoice is posted.
        HttpResponseMessage billed = await client.PostAsJsonAsync($"{Cards}/{id}/sales-invoice", new { });
        billed.StatusCode.ShouldBe(HttpStatusCode.Created, await billed.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await billed.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        invoice.Taxable.ShouldBe(60m);
        (await OnHandAsync(client, masters)).ShouldBe(10m);

        SalesInvoiceDetail draft = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}"))!;
        draft.ReferenceNumber.ShouldBe(card.Summary.Number);
        draft.CustomerLedgerId.ShouldBe(masters.CustomerId);

        // Once.
        (await client.PostAsJsonAsync($"{Cards}/{id}/sales-invoice", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The parts are the invoice's now; the card's other details can still change.
        (await client.PutAsJsonAsync($"{Cards}/{id}", Card(
            masters, mobile, technician, problem: "Display and frame", quantity: 5m)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        card = (await client.GetFromJsonAsync<JobCardDetail>($"{Cards}/{id}"))!;
        card.Lines.Single().Quantity.ShouldBe(2m);
        card.Work.Problem.ShouldBe("Display and frame");

        // A billed job cannot be withdrawn.
        (await client.PostAsJsonAsync($"{Cards}/{id}/cancel", new { Reason = "Customer left" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        HttpResponseMessage posted = await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/post", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());
        (await OnHandAsync(client, masters)).ShouldBe(8m);

        (await client.PostAsJsonAsync($"{Cards}/{id}/stage", new { Stage = JobCardStage.Delivered, On = Today }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        List<JobCardSummary> delivered = (await client.GetFromJsonAsync<List<JobCardSummary>>(
            $"{Cards}?status={(int)JobCardStatus.Delivered}&search={mobile}"))!;
        delivered.Single().SalesInvoiceNumber.ShouldBe(invoice.Number);

        // Found again by the number they gave, with the account the job went on.
        List<JobCardCustomerMatch> found = (await client.GetFromJsonAsync<List<JobCardCustomerMatch>>(
            $"{Cards}/customers?mobile={mobile[2..]}"))!;
        found[0].CustomerLedgerId.ShouldBe(masters.CustomerId);
        found[0].Customer.Name.ShouldBe("Walk-in Ahmed");
    }

    [Fact]
    public async Task A_cancelled_invoice_lets_the_job_be_billed_again_and_a_walk_in_needs_an_account()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string mobile = $"05{Random.Shared.Next(10_000_000, 99_999_999)}";

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Cards, Card(masters, mobile, technician: null, account: false));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        // No account on the card and none given: refused.
        (await client.PostAsJsonAsync($"{Cards}/{id}/sales-invoice", new { }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        SalesInvoiceResponse first = (await (await client.PostAsJsonAsync(
            $"{Cards}/{id}/sales-invoice", new { CustomerLedgerId = masters.CustomerId }))
            .Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        (await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{first.SalesInvoiceId}/cancel", new { Reason = "Wrong rate" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage again = await client.PostAsJsonAsync(
            $"{Cards}/{id}/sales-invoice", new { CustomerLedgerId = masters.CustomerId });
        again.StatusCode.ShouldBe(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        SalesInvoiceResponse second = (await again.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        second.SalesInvoiceId.ShouldNotBe(first.SalesInvoiceId);

        // Cancelling its invoice too frees the job to be withdrawn.
        (await client.PostAsJsonAsync(
            $"/api/v1/sales/invoices/{second.SalesInvoiceId}/cancel", new { Reason = "Customer left" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"{Cards}/{id}/cancel", new { Reason = "Customer took it away" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JobCardDetail card = (await client.GetFromJsonAsync<JobCardDetail>($"{Cards}/{id}"))!;
        card.Summary.Status.ShouldBe(JobCardStatus.Cancelled);
        card.CancellationReason.ShouldBe("Customer took it away");

        (await client.PostAsJsonAsync($"{Cards}/{id}/stage", new { Stage = JobCardStage.Ready, On = Today }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_service_sale_carries_its_tax_mode_and_additional_ledgers_onto_a_paid_bill()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);
        string mobile = $"05{Random.Shared.Next(10_000_000, 99_999_999)}";
        await ReceiveAsync(client, masters, 5m);

        List<ChargeSetup> setup = (await client.GetFromJsonAsync<List<ChargeSetup>>(
            "/api/v1/accounting/charges?document=Sales"))!;
        Guid freight = setup.Single(charge => charge.Code == "FREIGHT").LedgerId;
        Guid discount = setup.Single(charge => charge.Code == "DISC-ALLOWED").LedgerId;

        // Billed without tax, with a service charge of 10 and 4 off.
        HttpResponseMessage created = await client.PostAsJsonAsync(Cards, Sale(masters, mobile, new[]
        {
            new { LedgerId = freight, Amount = 10m },
            new { LedgerId = discount, Amount = 4m },
        }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        Guid id = await created.Content.ReadFromJsonAsync<Guid>();

        JobCardDetail card = (await client.GetFromJsonAsync<JobCardDetail>($"{Cards}/{id}"))!;
        card.Mode.ShouldBe(TaxMode.NonTax);
        card.Charges.ShouldNotBeNull().Count.ShouldBe(2);
        card.Charges.Single(charge => charge.LedgerId == discount).IsAddition.ShouldBeFalse();
        card.Summary.Amount.ShouldBe(30m);
        card.Summary.ChargeTotal.ShouldBe(6m);

        // A ledger that is not set up for a sale is refused by name.
        HttpResponseMessage stray = await client.PutAsJsonAsync($"{Cards}/{id}", Sale(masters, mobile, new[]
        {
            new { LedgerId = masters.CustomerId, Amount = 1m },
        }));
        stray.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stray.Content.ReadAsStringAsync()).ShouldContain("JobCard.ChargeNotSetUp");

        // Billed and paid in full in cash: 30 of parts, no tax, +10 -4.
        Guid cash = (await client.GetFromJsonAsync<List<LedgerSummary>>("/api/v1/accounting/ledgers"))!
            .First(ledger => ledger.Kind == LedgerKind.Cash).LedgerId;

        HttpResponseMessage billed = await client.PostAsJsonAsync($"{Cards}/{id}/sales-invoice", new
        {
            PaidInFull = true,
            ReceivedIntoLedgerId = cash,
        });
        billed.StatusCode.ShouldBe(HttpStatusCode.Created, await billed.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await billed.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;
        invoice.Tax.ShouldBe(0m);
        invoice.ChargeTotal.ShouldBe(6m);
        invoice.Total.ShouldBe(36m);

        SalesInvoiceDetail detail = (await client.GetFromJsonAsync<SalesInvoiceDetail>(
            $"/api/v1/sales/invoices/{invoice.SalesInvoiceId}"))!;
        detail.Mode.ShouldBe(TaxMode.NonTax);
        detail.AmountReceived.ShouldBe(36m);

        (await client.PostAsync($"/api/v1/sales/invoices/{invoice.SalesInvoiceId}/post", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static object Sale(TestMasters masters, string mobile, object charges) => new
    {
        Date = Today,
        masters.WarehouseId,
        CustomerLedgerId = masters.CustomerId,
        Customer = new { Name = "Counter customer", Mobile = mobile },
        PaymentMode = SalesPaymentMode.Cash,
        Device = new { ItemName = "Laptop" },
        Work = new { ServiceType = "Repair", Fault = "No power" },
        Lines = new[] { new { masters.ProductId, Quantity = 1m, Rate = 30m } },
        Mode = TaxMode.NonTax,
        Charges = charges,
    };

    private static object Card(
        TestMasters masters,
        string mobile,
        Guid? technician,
        string name = "Walk-in Ahmed",
        string? problem = null,
        decimal quantity = 1m,
        bool account = true) => new
        {
            Date = Today,
            masters.WarehouseId,
            CustomerLedgerId = account ? masters.CustomerId : (Guid?)null,
            Customer = new { Name = name, Mobile = mobile, AddressLine1 = "Souq street" },
            PaymentMode = SalesPaymentMode.Cash,
            Device = new { ItemName = "Mobile phone", BrandName = "Acme", ModelNumber = "A1", SerialNumber = "IMEI-1" },
            Work = new { ServiceType = "Repair", Fault = "Screen broken", Problem = problem, EstimatedCharge = 75m },
            TechnicianLedgerId = technician,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 30m, Remarks = "Display" } },
        };

    private static async Task<Guid> EmployeeAsync(HttpClient client)
    {
        Guid expenses = (await client.GetFromJsonAsync<List<AccountGroupSummary>>(
            "/api/v1/accounting/account-groups"))!.Single(group => group.Code == "5900").AccountGroupId;

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/accounting/ledgers", new
        {
            Name = $"Technician {Guid.NewGuid().ToString("N")[..6]}",
            AccountGroupId = expenses,
            Kind = LedgerKind.Employee,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<LedgerDetail>())!.LedgerId;
    }

    private static async Task ReceiveAsync(HttpClient client, TestMasters masters, decimal quantity)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/inventory/stock/documents", new
        {
            Type = StockDocumentType.MaterialReceipt,
            Date = Today,
            masters.WarehouseId,
            Lines = new[] { new { masters.ProductId, Quantity = quantity, Rate = 20m } },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<decimal> OnHandAsync(HttpClient client, TestMasters masters) =>
        (await client.GetFromJsonAsync<StockValuationReport>(
            $"/api/v1/inventory/stock/valuation?warehouseId={masters.WarehouseId}&includeZero=true"))!
            .Rows.Where(row => row.ProductId == masters.ProductId)
            .Sum(row => row.Quantity);
}
