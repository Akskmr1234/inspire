using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using FluentValidation;

namespace ERP.Application.Sales;

/// <summary>How to reach a customer, and where to send their goods.</summary>
/// <param name="MobileNumber">The number a counter looks them up by.</param>
/// <param name="Phone">A landline, where there is one.</param>
/// <param name="Email">An email address.</param>
/// <param name="AddressLine1">The first line of the address.</param>
/// <param name="AddressLine2">The second.</param>
public sealed record CustomerContact(
    string? MobileNumber = null,
    string? Phone = null,
    string? Email = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null);

/// <summary>What a customer owes on, and for how long.</summary>
/// <param name="CreditLimit">The exposure the firm is willing to carry. Warns; never blocks.</param>
/// <param name="CreditDays">How long they have to pay, which dates every bill raised.</param>
/// <param name="IsBillWise">Whether receipts are allocated against individual bills.</param>
public sealed record CustomerTerms(
    decimal? CreditLimit = null,
    int? CreditDays = null,
    bool IsBillWise = true);

/// <summary>The registration details a tax document needs.</summary>
/// <param name="RegistrationNumber">Their VAT number or GSTIN.</param>
/// <param name="StateCode">
/// Their state or emirate. Compared with the firm's to decide IGST against CGST plus SGST,
/// so on a GST firm this is the field that decides which heads an invoice charges.
/// </param>
public sealed record CustomerTaxDetails(
    string? RegistrationNumber = null,
    string? StateCode = null);

/// <summary>Where a customer is, and how else they are reached and registered.</summary>
/// <param name="City">Their town or city.</param>
/// <param name="PinCode">Their postal or PIN code.</param>
/// <param name="Website">Their website.</param>
/// <param name="Fax">Their fax number.</param>
/// <param name="ContactPerson">The person to ask for.</param>
/// <param name="GstNumber">Their GST identification number.</param>
/// <param name="TinNumber">Their taxpayer identification number.</param>
/// <param name="CstNumber">Their CST registration number.</param>
/// <param name="CountryCode">The ISO code of their country, from the country master.</param>
/// <param name="AreaId">The area they are in.</param>
/// <param name="RouteId">The route they are on.</param>
/// <param name="SendsSms">Whether they are sent text messages.</param>
public sealed record CustomerProfile(
    string? City = null,
    string? PinCode = null,
    string? Website = null,
    string? Fax = null,
    string? ContactPerson = null,
    string? GstNumber = null,
    string? TinNumber = null,
    string? CstNumber = null,
    string? CountryCode = null,
    Guid? AreaId = null,
    Guid? RouteId = null,
    bool SendsSms = false);

/// <summary>Creates a customer.</summary>
/// <param name="Code">The account code, unique within the firm; the next CUS- number when left blank.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic, for a bilingual invoice.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What they owe on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <param name="AccountGroupId">
/// The group they report under. Omit to use the firm's seeded debtors group.
/// </param>
/// <param name="OpeningBalance">What they already owed when the system was taken on.</param>
/// <param name="Profile">Where they are, and their other numbers.</param>
/// <remarks>
/// A customer is a sub-ledger, which is what section 7.1 calls it and what the rest of the
/// system already assumes: an invoice is billed to a ledger, a receipt settles against one,
/// and the debtors report sums them. Making a customer a record of its own beside a ledger
/// would mean two things to keep in step and two answers to what a customer owes.
/// </remarks>
public sealed record CreateCustomerCommand(
    string? Code,
    string Name,
    string? NameArabic = null,
    CustomerContact? Contact = null,
    CustomerTerms? Terms = null,
    CustomerTaxDetails? TaxDetails = null,
    Guid? AccountGroupId = null,
    decimal OpeningBalance = 0m,
    CustomerProfile? Profile = null) : ICommand<CustomerResponse>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.Ledger;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => null;
}

/// <summary>Changes a customer's details.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What they owe on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <param name="Profile">Where they are, and their other numbers.</param>
/// <param name="OpeningBalance">
/// What they owed when the books were taken on, corrected. Omit to leave it as it is.
/// </param>
/// <remarks>
/// The code is not among these. It is what a firm's own records and any imported history
/// refer to a customer by, and renaming it silently would leave both pointing at nothing.
/// </remarks>
public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    string Name,
    string? NameArabic = null,
    CustomerContact? Contact = null,
    CustomerTerms? Terms = null,
    CustomerTaxDetails? TaxDetails = null,
    CustomerProfile? Profile = null,
    decimal? OpeningBalance = null) : ICommand<CustomerResponse>, ITransactional, INamesMaster
{
    /// <inheritdoc />
    NamedMaster INamesMaster.Master => NamedMaster.Ledger;

    /// <inheritdoc />
    string INamesMaster.NameGiven => Name;

    /// <inheritdoc />
    Guid? INamesMaster.RenamedId => CustomerId;
}

/// <summary>Withdraws a customer from use, or puts them back.</summary>
/// <param name="CustomerId">The customer.</param>
/// <param name="IsActive">Whether they may be billed.</param>
/// <remarks>
/// Withdrawn rather than deleted, always. A customer with history is what a debtors report
/// and every past invoice point at, and deleting one would take an audit trail with it -
/// so this only decides whether a new document may name them.
/// </remarks>
public sealed record SetCustomerActiveCommand(Guid CustomerId, bool IsActive)
    : ICommand<CustomerResponse>, ITransactional;

/// <summary>Lists customers, for a picker or a counter's lookup.</summary>
/// <param name="Search">Matched against code, name and mobile number.</param>
/// <param name="ActiveOnly">Whether to exclude withdrawn customers.</param>
public sealed record GetCustomersQuery(string? Search = null, bool ActiveOnly = true)
    : IQuery<IReadOnlyList<CustomerResponse>>;

/// <summary>Reads one customer.</summary>
/// <param name="CustomerId">The customer.</param>
public sealed record GetCustomerQuery(Guid CustomerId) : IQuery<CustomerResponse>;

/// <summary>A customer as the system holds them.</summary>
/// <param name="CustomerId">The customer's ledger.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What they owe on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <param name="Currency">The currency their account is kept in.</param>
/// <param name="OpeningBalance">What they owed when the system was taken on.</param>
/// <param name="IsActive">Whether they may be billed.</param>
/// <param name="Profile">Where they are, and their other numbers.</param>
public sealed record CustomerResponse(
    Guid CustomerId,
    string Code,
    string Name,
    string? NameArabic,
    CustomerContact Contact,
    CustomerTerms Terms,
    CustomerTaxDetails TaxDetails,
    string Currency,
    decimal OpeningBalance,
    bool IsActive,
    CustomerProfile Profile);

/// <summary>Validates <see cref="CreateCustomerCommand"/>.</summary>
public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateCustomerCommandValidator"/> class.</summary>
    public CreateCustomerCommandValidator()
    {
        RuleFor(c => c.Code!)
            .MaximumLength(30)
            .When(c => c.Code is not null);

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("A customer name is required.")
            .MaximumLength(200);

        RuleFor(c => c.OpeningBalance)
            .GreaterThanOrEqualTo(0m)
            .WithMessage(
                "An opening balance is entered as a positive amount. A customer in credit "
                + "is a payable, not a negative receivable.");

        Include(new CustomerDetailsValidator<CreateCustomerCommand>(
            c => c.Contact, c => c.Terms, c => c.TaxDetails));
    }
}

/// <summary>Validates <see cref="UpdateCustomerCommand"/>.</summary>
public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateCustomerCommandValidator"/> class.</summary>
    public UpdateCustomerCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEqual(Guid.Empty);

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("A customer name is required.")
            .MaximumLength(200);

        Include(new CustomerDetailsValidator<UpdateCustomerCommand>(
            c => c.Contact, c => c.Terms, c => c.TaxDetails));

        RuleFor(c => c.OpeningBalance!.Value)
            .GreaterThanOrEqualTo(0m)
            .When(c => c.OpeningBalance is not null)
            .WithMessage("An opening balance is entered as a positive amount.");
    }
}

/// <summary>The rules shared by creating a customer and changing one.</summary>
/// <typeparam name="TCommand">The command being validated.</typeparam>
/// <remarks>
/// Shared rather than copied, because the two commands accept the same three blocks and
/// two copies of a credit-days rule would eventually disagree about what a negative one
/// means.
/// </remarks>
internal sealed class CustomerDetailsValidator<TCommand> : AbstractValidator<TCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CustomerDetailsValidator{TCommand}"/> class.</summary>
    /// <param name="contact">Reads the contact block off the command.</param>
    /// <param name="terms">Reads the terms block.</param>
    /// <param name="taxDetails">Reads the tax block.</param>
    internal CustomerDetailsValidator(
        Func<TCommand, CustomerContact?> contact,
        Func<TCommand, CustomerTerms?> terms,
        Func<TCommand, CustomerTaxDetails?> taxDetails)
    {
        RuleFor(c => contact(c)!).ChildRules(block =>
        {
            block.RuleFor(b => b.MobileNumber).MaximumLength(30);
            block.RuleFor(b => b.Phone).MaximumLength(30);
            block.RuleFor(b => b.Email).MaximumLength(200);
            block.RuleFor(b => b.AddressLine1).MaximumLength(200);
            block.RuleFor(b => b.AddressLine2).MaximumLength(200);
        }).When(c => contact(c) is not null);

        RuleFor(c => terms(c)!).ChildRules(block =>
        {
            block.RuleFor(b => b.CreditLimit!.Value)
                .GreaterThanOrEqualTo(0m)
                .When(b => b.CreditLimit is not null)
                .WithMessage("A credit limit cannot be negative.");

            block.RuleFor(b => b.CreditDays!.Value)
                .GreaterThanOrEqualTo(0)
                .When(b => b.CreditDays is not null)
                .WithMessage("Credit days cannot be negative.");
        }).When(c => terms(c) is not null);

        RuleFor(c => taxDetails(c)!).ChildRules(block =>
        {
            block.RuleFor(b => b.RegistrationNumber).MaximumLength(50);
            block.RuleFor(b => b.StateCode).MaximumLength(10);
        }).When(c => taxDetails(c) is not null);
    }
}
