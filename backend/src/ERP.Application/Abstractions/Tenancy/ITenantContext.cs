using ERP.SharedKernel.Tenancy;

namespace ERP.Application.Abstractions.Tenancy;

/// <summary>
/// The tenant, firm, and branch the current operation is acting within.
/// </summary>
/// <remarks>
/// <para>
/// Resolved once per request from the authenticated user's claims and then
/// treated as ambient. Every tenant-scoped query is filtered by
/// <see cref="TenantId"/> automatically, so no handler needs to remember to add
/// a <c>Where</c> clause - and, more to the point, no handler can forget to.
/// </para>
/// <para>
/// Firm and branch are nullable because they are not always known. A user who
/// has just signed in but not yet chosen a firm has a tenant and nothing else;
/// a platform-administration endpoint listing every firm operates above the firm
/// level by design.
/// </para>
/// </remarks>
public interface ITenantContext
{
    /// <summary>Gets the tenant the operation belongs to.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no tenant has been resolved. Deliberately throwing rather than
    /// returning an empty identifier: an unresolved tenant that silently reads as
    /// <see cref="Guid.Empty"/> would produce a query matching no rows, which
    /// looks like "no data" instead of the configuration fault it actually is.
    /// </exception>
    TenantId TenantId { get; }

    /// <summary>Gets the firm in scope, if one has been selected.</summary>
    FirmId? FirmId { get; }

    /// <summary>Gets the branch in scope, if one has been selected.</summary>
    BranchId? BranchId { get; }

    /// <summary>
    /// Gets a value indicating whether a tenant has been resolved. Check this
    /// before reading <see cref="TenantId"/> where absence is legitimate.
    /// </summary>
    bool IsResolved { get; }

    /// <summary>
    /// Enters a tenant scope explicitly, restoring the previous scope when the
    /// returned handle is disposed.
    /// </summary>
    /// <param name="tenantId">The tenant to act as.</param>
    /// <param name="firmId">The firm to act within, if any.</param>
    /// <param name="branchId">The branch to act within, if any.</param>
    /// <returns>A handle that restores the previous scope on disposal.</returns>
    /// <remarks>
    /// For work with no HTTP request to resolve from: a scheduled Hangfire job
    /// processing each tenant in turn, a data migration, an integration test.
    /// <para>
    /// This exists instead of a "bypass the filters" flag on purpose. Disabling
    /// isolation is never the right answer; acting deliberately as a named tenant
    /// is. Code that needs to touch several tenants opens one scope per tenant,
    /// which keeps every query filtered and leaves the intent legible at the call
    /// site.
    /// </para>
    /// </remarks>
    IDisposable BeginScope(TenantId tenantId, FirmId? firmId = null, BranchId? branchId = null);
}
