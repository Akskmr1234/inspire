using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Tenancy;

/// <summary>The commercial state of a tenant's subscription.</summary>
public enum SubscriptionStatus
{
    /// <summary>Evaluating, with full access until the trial ends.</summary>
    Trial = 1,

    /// <summary>Paid and current.</summary>
    Active = 2,

    /// <summary>
    /// Lapsed. The reference application shows a banner and keeps the data
    /// readable, so this is a soft state rather than a lockout.
    /// </summary>
    Expired = 3,

    /// <summary>Administratively suspended. Sign-in is refused.</summary>
    Suspended = 4,
}

/// <summary>
/// The registry entry for one customer of the platform - the outermost isolation
/// boundary, containing one or more firms.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> <see cref="SharedKernel.Abstractions.ITenantScoped"/>,
/// and deliberately carries no row-level-security policy. It cannot: this is the
/// table that answers "which tenant is this?", and a query filtered by the tenant
/// you have not yet identified can never return anything.
/// </para>
/// <para>
/// That is what makes sign-in possible at all. Authentication faces a
/// chicken-and-egg problem - the tenant is needed to find the user, but the user
/// is what identifies the tenant. Resolving a tenant <em>code</em> against this
/// unfiltered registry breaks the cycle without weakening isolation anywhere
/// else: nothing here is confidential, and every subsequent query runs inside a
/// properly established tenant scope.
/// </para>
/// <para>
/// The alternative designs were worse. Making users globally readable would
/// remove isolation from the most sensitive table in the system; a
/// <c>BYPASSRLS</c> role or a permissive policy exception would put a permanent
/// hole in the mechanism for the sake of one lookup.
/// </para>
/// </remarks>
public sealed class Tenant : AggregateRoot<TenantId>
{
    private Tenant(TenantId id, string code, string name, SubscriptionStatus status)
        : base(id)
    {
        Code = code;
        Name = name;
        SubscriptionStatus = status;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Tenant()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>
    /// Gets the short, URL-safe code identifying the tenant at sign-in, for
    /// example <c>startech</c>.
    /// </summary>
    /// <remarks>
    /// Lower-cased and globally unique. This is what a user types as "Company" on
    /// the sign-in screen, or what a subdomain maps to in a hosted deployment.
    /// </remarks>
    public string Code { get; private set; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the subscription state.</summary>
    public SubscriptionStatus SubscriptionStatus { get; private set; }

    /// <summary>Gets the date the subscription lapses, if it is time-limited.</summary>
    public DateOnly? SubscriptionEndsOn { get; private set; }

    /// <summary>Gets a value indicating whether the tenant may be signed in to.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Gets the instant the tenant was registered, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Creates a tenant.</summary>
    /// <param name="code">The sign-in code, unique across the platform.</param>
    /// <param name="name">The display name.</param>
    /// <param name="status">The initial subscription state.</param>
    /// <returns>The tenant, or a validation failure.</returns>
    public static Result<Tenant> Create(
        string code,
        string name,
        SubscriptionStatus status = SubscriptionStatus.Trial)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<Tenant>(Error.Validation(
                "Tenant.CodeRequired", "A tenant code is required."));
        }

        string normalised = code.Trim().ToLowerInvariant();

        if (normalised.Length is < 2 or > 40)
        {
            return Result.Failure<Tenant>(Error.Validation(
                "Tenant.CodeLength", "A tenant code must be between 2 and 40 characters."));
        }

        // Restricted to characters that are safe in a subdomain and a URL path,
        // because the code is used in both.
        if (!normalised.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-'))
        {
            return Result.Failure<Tenant>(Error.Validation(
                "Tenant.CodeInvalid",
                "A tenant code may contain only lower-case letters, digits, and hyphens."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tenant>(Error.Validation(
                "Tenant.NameRequired", "A tenant name is required."));
        }

        return Result.Success(new Tenant(TenantId.NewId(), normalised, name.Trim(), status));
    }

    /// <summary>Determines whether sign-in is currently permitted.</summary>
    /// <param name="today">The current date in the tenant's own terms.</param>
    /// <returns>Success, or the reason sign-in is refused.</returns>
    /// <remarks>
    /// An expired subscription does <em>not</em> refuse sign-in. The reference
    /// application shows a renewal banner and leaves the data reachable, which is
    /// the humane behaviour: locking a business out of its own accounts over a
    /// lapsed invoice causes far more damage than it recovers. Only an explicit
    /// administrative suspension blocks access.
    /// </remarks>
    public Result EnsureCanSignIn(DateOnly today)
    {
        if (!IsActive)
        {
            return Result.Failure(Error.Forbidden(
                "Tenant.Inactive", "This account is not active."));
        }

        if (SubscriptionStatus == SubscriptionStatus.Suspended)
        {
            return Result.Failure(Error.Forbidden(
                "Tenant.Suspended", "This account has been suspended."));
        }

        if (SubscriptionEndsOn is { } ends && ends < today
            && SubscriptionStatus != SubscriptionStatus.Expired)
        {
            SubscriptionStatus = SubscriptionStatus.Expired;
        }

        return Result.Success();
    }

    /// <summary>Records a subscription renewal.</summary>
    /// <param name="endsOn">The new end date, or <see langword="null"/> for perpetual.</param>
    public void Renew(DateOnly? endsOn)
    {
        SubscriptionStatus = SubscriptionStatus.Active;
        SubscriptionEndsOn = endsOn;
    }

    /// <summary>Suspends the tenant, refusing further sign-in.</summary>
    public void Suspend() => SubscriptionStatus = SubscriptionStatus.Suspended;

    /// <summary>Deactivates the tenant entirely.</summary>
    public void Deactivate() => IsActive = false;
}
