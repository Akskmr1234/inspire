using ERP.Application.Abstractions.Messaging;
using FluentValidation;

namespace ERP.Application.Purchase;

/// <summary>How to reach a supplier, and where their goods come from.</summary>
/// <param name="MobileNumber">A mobile number.</param>
/// <param name="Phone">A landline, where there is one.</param>
/// <param name="Email">An email address.</param>
/// <param name="AddressLine1">The first line of the address.</param>
/// <param name="AddressLine2">The second.</param>
public sealed record SupplierContact(
    string? MobileNumber = null,
    string? Phone = null,
    string? Email = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null);

/// <summary>What the firm owes a supplier on, and for how long.</summary>
/// <param name="CreditLimit">The exposure the supplier is willing to carry. Warns; never blocks.</param>
/// <param name="CreditDays">How long the firm has to pay, which dates every bill raised.</param>
/// <param name="IsBillWise">Whether payments are allocated against individual bills.</param>
public sealed record SupplierTerms(
    decimal? CreditLimit = null,
    int? CreditDays = null,
    bool IsBillWise = true);

/// <summary>The registration details a tax document needs.</summary>
/// <param name="RegistrationNumber">Their VAT number or GSTIN.</param>
/// <param name="StateCode">
/// Their state or emirate. Compared with the firm's to decide IGST against CGST plus SGST,
/// so on a GST firm this is the field that decides which heads a purchase reclaims.
/// </param>
public sealed record SupplierTaxDetails(
    string? RegistrationNumber = null,
    string? StateCode = null);

/// <summary>Creates a supplier.</summary>
/// <param name="Code">The account code, unique within the firm.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic, for a bilingual document.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What the firm owes on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <param name="AccountGroupId">
/// The group they report under. Omit to use the firm's seeded creditors group.
/// </param>
/// <param name="OpeningBalance">What was already owed to them when the system was taken on.</param>
/// <remarks>
/// A supplier is a sub-ledger, as a customer is and for the same reasons: a purchase is
/// billed by a ledger, a payment settles against one, and the creditors report sums them.
/// </remarks>
public sealed record CreateSupplierCommand(
    string Code,
    string Name,
    string? NameArabic = null,
    SupplierContact? Contact = null,
    SupplierTerms? Terms = null,
    SupplierTaxDetails? TaxDetails = null,
    Guid? AccountGroupId = null,
    decimal OpeningBalance = 0m) : ICommand<SupplierResponse>, ITransactional;

/// <summary>Changes a supplier's details.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What the firm owes on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <remarks>
/// The code is not among these, for the reason a customer's is not: it is what a firm's
/// own records and any imported history refer to a supplier by.
/// </remarks>
public sealed record UpdateSupplierCommand(
    Guid SupplierId,
    string Name,
    string? NameArabic = null,
    SupplierContact? Contact = null,
    SupplierTerms? Terms = null,
    SupplierTaxDetails? TaxDetails = null) : ICommand<SupplierResponse>, ITransactional;

/// <summary>Withdraws a supplier from use, or puts them back.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="IsActive">Whether they may be bought from.</param>
public sealed record SetSupplierActiveCommand(Guid SupplierId, bool IsActive)
    : ICommand<SupplierResponse>, ITransactional;

/// <summary>Lists suppliers, for a picker.</summary>
/// <param name="Search">Matched against code, name and mobile number.</param>
/// <param name="ActiveOnly">Whether to exclude withdrawn suppliers.</param>
public sealed record GetSuppliersQuery(string? Search = null, bool ActiveOnly = true)
    : IQuery<IReadOnlyList<SupplierResponse>>;

/// <summary>Reads one supplier.</summary>
/// <param name="SupplierId">The supplier.</param>
public sealed record GetSupplierQuery(Guid SupplierId) : IQuery<SupplierResponse>;

/// <summary>A supplier as the system holds them.</summary>
/// <param name="SupplierId">The supplier's ledger.</param>
/// <param name="Code">The account code.</param>
/// <param name="Name">Their name.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="Contact">How to reach them.</param>
/// <param name="Terms">What the firm owes on.</param>
/// <param name="TaxDetails">Their registration.</param>
/// <param name="Currency">The currency their account is kept in.</param>
/// <param name="OpeningBalance">What was owed when the system was taken on.</param>
/// <param name="IsActive">Whether they may be bought from.</param>
public sealed record SupplierResponse(
    Guid SupplierId,
    string Code,
    string Name,
    string? NameArabic,
    SupplierContact Contact,
    SupplierTerms Terms,
    SupplierTaxDetails TaxDetails,
    string Currency,
    decimal OpeningBalance,
    bool IsActive);

/// <summary>Validates <see cref="CreateSupplierCommand"/>.</summary>
public sealed class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateSupplierCommandValidator"/> class.</summary>
    public CreateSupplierCommandValidator()
    {
        RuleFor(c => c.Code)
            .NotEmpty().WithMessage("A supplier code is required.")
            .MaximumLength(30);

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("A supplier name is required.")
            .MaximumLength(200);

        RuleFor(c => c.OpeningBalance)
            .GreaterThanOrEqualTo(0m)
            .WithMessage(
                "An opening balance is entered as a positive amount. A supplier the firm "
                + "is in credit with is a receivable, not a negative payable.");

        Include(new SupplierDetailsValidator<CreateSupplierCommand>(
            c => c.Contact, c => c.Terms, c => c.TaxDetails));
    }
}

/// <summary>Validates <see cref="UpdateSupplierCommand"/>.</summary>
public sealed class UpdateSupplierCommandValidator : AbstractValidator<UpdateSupplierCommand>
{
    /// <summary>Initialises a new instance of the <see cref="UpdateSupplierCommandValidator"/> class.</summary>
    public UpdateSupplierCommandValidator()
    {
        RuleFor(c => c.SupplierId).NotEqual(Guid.Empty);

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("A supplier name is required.")
            .MaximumLength(200);

        Include(new SupplierDetailsValidator<UpdateSupplierCommand>(
            c => c.Contact, c => c.Terms, c => c.TaxDetails));
    }
}

/// <summary>The rules shared by creating a supplier and changing one.</summary>
/// <typeparam name="TCommand">The command being validated.</typeparam>
internal sealed class SupplierDetailsValidator<TCommand> : AbstractValidator<TCommand>
{
    /// <summary>Initialises a new instance of the <see cref="SupplierDetailsValidator{TCommand}"/> class.</summary>
    /// <param name="contact">Reads the contact block off the command.</param>
    /// <param name="terms">Reads the terms block.</param>
    /// <param name="taxDetails">Reads the tax block.</param>
    internal SupplierDetailsValidator(
        Func<TCommand, SupplierContact?> contact,
        Func<TCommand, SupplierTerms?> terms,
        Func<TCommand, SupplierTaxDetails?> taxDetails)
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
