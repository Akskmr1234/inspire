using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Application.Sales;
using ERP.Domain.Accounting;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Application.Tests.Sales;

/// <summary>Tests for the customer master of section 12.1.</summary>
/// <remarks>
/// A customer is a sub-ledger, so most of what could go wrong here is a ledger reached
/// through the wrong door: another firm's group, an account that is not a customer at all,
/// or a code somebody has already used. Those are what these cover, along with the one
/// behaviour a maintenance screen gets wrong most often - clearing a field the caller did
/// not send.
/// </remarks>
public sealed class CustomerTests
{
    [Fact]
    public async Task A_customer_is_created_under_the_firm_s_debtors_group()
    {
        CustomerFixture fixture = new();

        Result<CustomerResponse> result = await fixture.Create(
            code: "CUST-1",
            name: "Al Mansoor Trading",
            contact: new CustomerContact(MobileNumber: "55512345"),
            terms: new CustomerTerms(CreditLimit: 50_000m, CreditDays: 30));

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        result.Value.Code.ShouldBe("CUST-1");
        result.Value.Contact.MobileNumber.ShouldBe("55512345");
        result.Value.Terms.CreditDays.ShouldBe(30);

        Ledger created = fixture.Added.ShouldHaveSingleItem();

        created.Kind.ShouldBe(LedgerKind.Customer);
        created.AccountGroupId.ShouldBe(fixture.Debtors.Id);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_code_another_account_already_uses_is_refused()
    {
        // The commonest mistake at a counter: entering a customer somebody has already
        // created. Reported as itself rather than as a unique-index violation.
        CustomerFixture fixture = new(codeInUse: true);

        Result<CustomerResponse> result = await fixture.Create("CUST-1", "Al Mansoor");

        result.Error.Code.ShouldBe("Customer.CodeInUse");
        fixture.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_firm_with_no_debtors_group_is_told_to_name_one()
    {
        CustomerFixture fixture = new(hasDebtorsGroup: false);

        Result<CustomerResponse> result = await fixture.Create("CUST-1", "Al Mansoor");

        result.Error.Code.ShouldBe("Customer.NoDebtorsGroup");
    }

    [Fact]
    public async Task An_account_group_belonging_to_another_firm_is_refused()
    {
        // A customer reporting under another firm's group would corrupt both firms'
        // statements, and the identifier arrives from a client that could name anything.
        CustomerFixture fixture = new();

        Result<CustomerResponse> result = await fixture.Create(
            "CUST-1", "Al Mansoor", accountGroupId: Guid.NewGuid());

        result.Error.Code.ShouldBe("Customer.GroupNotFound");
    }

    [Fact]
    public async Task An_opening_balance_is_carried_as_a_receivable()
    {
        CustomerFixture fixture = new();

        await fixture.Create("CUST-1", "Al Mansoor", openingBalance: 1_500m);

        Ledger created = fixture.Added.ShouldHaveSingleItem();

        created.OpeningBalance.ShouldBe(1_500m);
        created.OpeningBalanceSide.ShouldBe(EntrySide.Debit);
    }

    [Fact]
    public async Task Details_the_caller_did_not_send_are_left_alone()
    {
        // What a whole-record update gets wrong: a screen that carries only an address
        // would otherwise drop the credit terms somebody agreed with the customer.
        CustomerFixture fixture = new();

        await fixture.Create(
            "CUST-1", "Al Mansoor",
            terms: new CustomerTerms(CreditLimit: 50_000m, CreditDays: 30));

        Guid customerId = fixture.Added[0].Id.Value;

        Result<CustomerResponse> updated = await fixture.Update(
            customerId, "Al Mansoor Trading LLC",
            contact: new CustomerContact(AddressLine1: "Salwa Road"));

        updated.Value.Name.ShouldBe("Al Mansoor Trading LLC");
        updated.Value.Contact.AddressLine1.ShouldBe("Salwa Road");
        updated.Value.Terms.CreditDays.ShouldBe(30);
        updated.Value.Terms.CreditLimit.ShouldBe(50_000m);
    }

    [Fact]
    public async Task The_code_survives_a_change_of_name()
    {
        CustomerFixture fixture = new();

        await fixture.Create("CUST-1", "Al Mansoor");

        Result<CustomerResponse> updated = await fixture.Update(
            fixture.Added[0].Id.Value, "Somebody Else Entirely");

        updated.Value.Code.ShouldBe("CUST-1");
    }

    [Fact]
    public async Task An_account_that_is_not_a_customer_is_not_found_through_this_door()
    {
        // A cash account reached here would be given credit terms and a mobile number,
        // and would then turn up in a customer picker.
        CustomerFixture fixture = new();

        Result<CustomerResponse> result = await fixture.Update(
            fixture.CashLedger.Id.Value, "Not a customer");

        result.Error.Code.ShouldBe("Customer.NotFound");
    }

    [Fact]
    public async Task A_withdrawn_customer_is_kept_and_read_back_as_withdrawn()
    {
        // Withdrawn rather than deleted: every past invoice and the debtors report point
        // at them, so this only decides whether a new document may name them.
        CustomerFixture fixture = new();

        await fixture.Create("CUST-1", "Al Mansoor");

        Guid customerId = fixture.Added[0].Id.Value;

        (await fixture.SetActive(customerId, isActive: false)).Value.IsActive.ShouldBeFalse();
        (await fixture.Get(customerId)).Value.IsActive.ShouldBeFalse();
        (await fixture.SetActive(customerId, isActive: true)).Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task A_counter_looks_a_customer_up_by_whatever_it_has()
    {
        CustomerFixture fixture = new();

        await fixture.Create(
            "CUST-1", "Al Mansoor", contact: new CustomerContact(MobileNumber: "55512345"));

        // The search reaches the repository as typed, and it is the repository that
        // matches it against the code, the name and the number.
        Result<IReadOnlyList<CustomerResponse>> found = await fixture.List("55512345");

        found.IsSuccess.ShouldBeTrue();
        await fixture.Ledgers.Received(1).ListByKindAsync(
            fixture.Firm.Id, LedgerKind.Customer, "55512345", true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_happens_until_a_firm_is_selected()
    {
        CustomerFixture fixture = new(firmSelected: false);

        (await fixture.Create("CUST-1", "Al Mansoor")).Error.Code
            .ShouldBe("Customer.NoFirmSelected");

        (await fixture.List()).Error.Code.ShouldBe("Customer.NoFirmSelected");
    }

    /// <summary>The customer handlers with their few dependencies substituted.</summary>
    private sealed class CustomerFixture
    {
        private static readonly TenantId Tenant = TenantId.NewId();

        private readonly Dictionary<LedgerId, Ledger> _ledgers = [];
        private readonly CreateCustomerCommandHandler _create;
        private readonly UpdateCustomerCommandHandler _update;
        private readonly SetCustomerActiveCommandHandler _setActive;
        private readonly GetCustomersQueryHandler _list;
        private readonly GetCustomerQueryHandler _get;

        internal CustomerFixture(
            bool firmSelected = true,
            bool codeInUse = false,
            bool hasDebtorsGroup = true)
        {
            Firm = Domain.Tenancy.Firm.Create(
                Tenant, "ACME", "Acme Trading", CurrencyCode.Qar,
                TaxRegime.GccVat, "Asia/Qatar").Value;

            Debtors = AccountGroup.CreateRoot(
                Tenant, Firm.Id, "1200", "Sundry Debtors", AccountNature.Asset).Value;

            CashLedger = Ledger.Create(
                AccountGroup.CreateRoot(
                    Tenant, Firm.Id, "1110", "Cash and Bank", AccountNature.Asset).Value,
                "CASH", "Cash in Hand", LedgerKind.Cash, CurrencyCode.Qar).Value;

            _ledgers[CashLedger.Id] = CashLedger;

            Ledgers = Substitute.For<ILedgerRepository>();
            Ledgers
                .FindAsync(Arg.Any<LedgerId>(), Arg.Any<CancellationToken>())
                .Returns(call => _ledgers.GetValueOrDefault(call.ArgAt<LedgerId>(0)));
            Ledgers
                .FindGroupAsync(Arg.Any<AccountGroupId>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                    call.ArgAt<AccountGroupId>(0) == Debtors.Id ? Debtors : null);
            Ledgers
                .FindGroupByCodeAsync(
                    Arg.Any<FirmId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(hasDebtorsGroup ? Debtors : null);
            Ledgers
                .IsCodeInUseAsync(
                    Arg.Any<FirmId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(codeInUse);
            Ledgers
                .ListByKindAsync(
                    Arg.Any<FirmId>(), Arg.Any<LedgerKind>(), Arg.Any<string?>(),
                    Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<Ledger>>(_ => [.. Added]);
            Ledgers.When(l => l.Add(Arg.Any<Ledger>())).Do(call =>
            {
                Ledger added = call.Arg<Ledger>();
                Added.Add(added);
                _ledgers[added.Id] = added;
            });

            IFirmRepository firms = Substitute.For<IFirmRepository>();
            firms.FindAsync(Firm.Id, Arg.Any<CancellationToken>()).Returns(Firm);

            UnitOfWork = Substitute.For<IUnitOfWork>();

            ITenantContext tenant = Substitute.For<ITenantContext>();
            tenant.IsResolved.Returns(true);
            tenant.TenantId.Returns(Tenant);
            tenant.FirmId.Returns(firmSelected ? Firm.Id : null);

            _create = new CreateCustomerCommandHandler(Ledgers, firms, tenant, UnitOfWork);
            _update = new UpdateCustomerCommandHandler(Ledgers, tenant, UnitOfWork);
            _setActive = new SetCustomerActiveCommandHandler(Ledgers, tenant, UnitOfWork);
            _list = new GetCustomersQueryHandler(Ledgers, tenant);
            _get = new GetCustomerQueryHandler(Ledgers, tenant);
        }

        internal Firm Firm { get; }

        internal AccountGroup Debtors { get; }

        internal Ledger CashLedger { get; }

        internal ILedgerRepository Ledgers { get; }

        internal IUnitOfWork UnitOfWork { get; }

        internal List<Ledger> Added { get; } = [];

        internal Task<Result<CustomerResponse>> Create(
            string code,
            string name,
            CustomerContact? contact = null,
            CustomerTerms? terms = null,
            Guid? accountGroupId = null,
            decimal openingBalance = 0m) =>
            _create.Handle(
                new CreateCustomerCommand(
                    code, name, Contact: contact, Terms: terms,
                    AccountGroupId: accountGroupId, OpeningBalance: openingBalance),
                CancellationToken.None);

        internal Task<Result<CustomerResponse>> Update(
            Guid customerId,
            string name,
            CustomerContact? contact = null,
            CustomerTerms? terms = null) =>
            _update.Handle(
                new UpdateCustomerCommand(customerId, name, Contact: contact, Terms: terms),
                CancellationToken.None);

        internal Task<Result<CustomerResponse>> SetActive(Guid customerId, bool isActive) =>
            _setActive.Handle(
                new SetCustomerActiveCommand(customerId, isActive), CancellationToken.None);

        internal Task<Result<IReadOnlyList<CustomerResponse>>> List(string? search = null) =>
            _list.Handle(new GetCustomersQuery(search), CancellationToken.None);

        internal Task<Result<CustomerResponse>> Get(Guid customerId) =>
            _get.Handle(new GetCustomerQuery(customerId), CancellationToken.None);
    }
}
