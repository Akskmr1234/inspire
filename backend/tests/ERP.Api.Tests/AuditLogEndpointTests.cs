using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ERP.Application.Platform.Audit;
using ERP.Application.Sales;
using ERP.Domain.Inventory;
using ERP.Infrastructure.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ERP.Api.Tests;

/// <summary>
/// The audit trail: every change to a master or a transaction written to a database of its
/// own, with the values before and after, and refused any change once written.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogEndpointTests
{
    private const string Audit = "/api/v1/platform/audit-log";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly ApiFactory _factory;

    /// <summary>Initialises a new instance of the <see cref="AuditLogEndpointTests"/> class.</summary>
    /// <param name="factory">The shared host.</param>
    public AuditLogEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task A_master_is_followed_from_creation_through_each_change_with_its_old_and_new_values()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        (await client.PutAsJsonAsync($"/api/v1/inventory/products/{masters.ProductId}/rates", new
        {
            CostingMethod = CostingMethod.AverageRate,
            Cost = 11m,
            RetailRate = 19m,
        })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        AuditLogPage page = (await client.GetFromJsonAsync<AuditLogPage>(
            $"{Audit}?module=Product&search={masters.ProductCode}"))!;

        page.Items.Select(entry => entry.Action).ShouldBe(["Update", "Create"]);

        AuditLogEntry created = page.Items[1];
        created.RecordLabel.ShouldBe(masters.ProductCode);
        created.RecordId.ShouldBe(masters.ProductId.ToString());
        created.UserName.ShouldBe(ApiFactory.AdminUserName);
        created.OldValues.ShouldBeNull();
        created.NewValues.ShouldNotBeNull().ShouldContain(masters.ProductCode);

        // An edit records only what changed, before and after.
        AuditLogEntry changed = page.Items[0];
        using JsonDocument before = JsonDocument.Parse(changed.OldValues.ShouldNotBeNull());
        using JsonDocument after = JsonDocument.Parse(changed.NewValues.ShouldNotBeNull());
        before.RootElement.GetProperty("Rates.RetailRate").GetDecimal().ShouldBe(0m);
        after.RootElement.GetProperty("Rates.RetailRate").GetDecimal().ShouldBe(19m);
        after.RootElement.TryGetProperty("Code", out _).ShouldBeFalse();

        AuditLogFilters filters = (await client.GetFromJsonAsync<AuditLogFilters>($"{Audit}/filters"))!;
        filters.Modules.ShouldContain("Product");
        filters.Modules.ShouldContain("Customer");
        filters.IsSeparateDatabase.ShouldBeTrue();
    }

    [Fact]
    public async Task An_edit_to_an_invoice_is_one_entry_that_says_which_lines_came_and_went()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        TestMasters masters = await TestMasters.ArrangeAsync(client);

        HttpResponseMessage drafted = await client.PostAsJsonAsync("/api/v1/sales/invoices", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Kind = 1,
            Lines = new[] { new { masters.ProductId, Quantity = 2m, Rate = 15m, TaxPercentage = 0m } },
        });
        drafted.StatusCode.ShouldBe(HttpStatusCode.Created, await drafted.Content.ReadAsStringAsync());
        SalesInvoiceResponse invoice = (await drafted.Content.ReadFromJsonAsync<SalesInvoiceResponse>())!;

        (await client.PutAsJsonAsync($"/api/v1/sales/invoices/{invoice.SalesInvoiceId}", new
        {
            Date = Today,
            CustomerLedgerId = masters.CustomerId,
            masters.WarehouseId,
            Lines = new[] { new { masters.ProductId, Quantity = 3m, Rate = 15m, TaxPercentage = 0m } },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);

        AuditLogPage page = (await client.GetFromJsonAsync<AuditLogPage>(
            $"{Audit}?module=Sales&search={invoice.Number}"))!;

        page.Items.Select(entry => entry.Action).ShouldBe(["Update", "Create"]);
        using JsonDocument before = JsonDocument.Parse(page.Items[0].OldValues.ShouldNotBeNull());
        using JsonDocument after = JsonDocument.Parse(page.Items[0].NewValues.ShouldNotBeNull());
        before.RootElement.GetProperty("SalesInvoiceLine")[0].GetProperty("Quantity").GetDecimal().ShouldBe(2m);
        after.RootElement.GetProperty("SalesInvoiceLine")[0].GetProperty("Quantity").GetDecimal().ShouldBe(3m);
    }

    [Fact]
    public async Task The_trail_refuses_to_be_changed_or_deleted_even_at_the_database()
    {
        HttpClient client = await _factory.CreateAuthenticatedClientAsync();
        await TestMasters.ArrangeAsync(client);

        AuditStore store = _factory.Services.GetRequiredService<AuditStore>();
        store.IsSeparate.ShouldBeTrue();

        await using NpgsqlConnection connection = new(store.ConnectionString);
        await connection.OpenAsync();

        foreach (string statement in new[]
        {
            "UPDATE audit.audit_entries SET action = 'Tampered'",
            "DELETE FROM audit.audit_entries",
            "TRUNCATE audit.audit_entries",
        })
        {
            await using NpgsqlCommand command = new(statement, connection);

            (await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync()))
                .MessageText.ShouldContain("cannot be changed or deleted");
        }

        await using NpgsqlCommand count = new("SELECT count(*) FROM audit.audit_entries", connection);
        ((long)(await count.ExecuteScalarAsync())!).ShouldBeGreaterThan(0);
    }
}
