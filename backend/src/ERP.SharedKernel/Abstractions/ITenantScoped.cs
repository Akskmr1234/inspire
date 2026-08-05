using ERP.SharedKernel.Tenancy;

namespace ERP.SharedKernel.Abstractions;

/// <summary>
/// Marks an entity as belonging to exactly one tenant. Implementing this is what
/// subjects a table to tenant isolation.
/// </summary>
/// <remarks>
/// <para>
/// Isolation is enforced twice, on purpose:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// An EF Core global query filter is applied to every type implementing this
/// interface, so no LINQ query can read another tenant's rows even if the
/// developer forgets a <c>Where</c> clause.
/// </description>
/// </item>
/// <item>
/// <description>
/// A PostgreSQL row-level-security policy on the same column, keyed off a
/// session variable, so raw SQL, a report query, a Hangfire job, or a
/// mistakenly-unfiltered <c>IgnoreQueryFilters()</c> call still cannot cross
/// the boundary.
/// </description>
/// </item>
/// </list>
/// <para>
/// One layer alone would be a single point of failure, and the failure mode -
/// one customer reading another customer's financial data - is the worst outcome
/// this system can produce. Hence belt and braces.
/// </para>
/// </remarks>
public interface ITenantScoped
{
    /// <summary>
    /// Gets the owning tenant. Stamped automatically on insert from the ambient
    /// tenant context; never set by hand in application code.
    /// </summary>
    TenantId TenantId { get; }
}

/// <summary>
/// Marks an entity as belonging to a specific firm within a tenant - separate
/// books, chart of accounts, numbering, and users.
/// </summary>
public interface IFirmScoped : ITenantScoped
{
    /// <summary>Gets the owning firm.</summary>
    FirmId FirmId { get; }
}

/// <summary>
/// Marks an entity as belonging to a specific branch, surfaced in the UI as
/// "Stock Location" or "Store Location".
/// </summary>
/// <remarks>
/// Branch-scoped rather than firm-scoped applies to transactional documents,
/// which originate at one location and carry branch-specific numbering.
/// Masters are usually firm-scoped so they are shared across branches.
/// </remarks>
public interface IBranchScoped : IFirmScoped
{
    /// <summary>Gets the owning branch.</summary>
    BranchId BranchId { get; }
}
