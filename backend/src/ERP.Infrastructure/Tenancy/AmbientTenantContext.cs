using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Tenancy;

namespace ERP.Infrastructure.Tenancy;

/// <summary>
/// The default <see cref="ITenantContext"/>, holding the current scope in an
/// <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AsyncLocal{T}"/> rather than a plain field so the scope follows the
/// logical flow of control across <c>await</c> boundaries and into spawned tasks.
/// A request that fans out to run three queries in parallel must carry its tenant
/// into all three.
/// </para>
/// <para>
/// Deliberately holds no reference to <c>HttpContext</c>. Resolving a tenant from
/// a JWT is the API layer's job; this type serves requests, background jobs, and
/// tests identically.
/// </para>
/// </remarks>
public sealed class AmbientTenantContext : ITenantContext
{
    // An instance field rather than a static one. The service is registered as a
    // singleton, so the behaviour is identical, but nothing here is global mutable
    // state: a test can construct its own instance without leaking into the next
    // test, and the lifetime is decided by the container rather than by the CLR.
    private readonly AsyncLocal<TenantScope?> _current = new();

    /// <inheritdoc />
    public TenantId TenantId => _current.Value?.TenantId
        ?? throw new InvalidOperationException(
            "No tenant has been resolved for the current operation. An authenticated " +
            "request should have one from its token; background work must establish " +
            "one with ITenantContext.BeginScope.");

    /// <inheritdoc />
    public FirmId? FirmId => _current.Value?.FirmId;

    /// <inheritdoc />
    public BranchId? BranchId => _current.Value?.BranchId;

    /// <inheritdoc />
    public bool IsResolved => _current.Value is not null;

    /// <inheritdoc />
    public IDisposable BeginScope(
        TenantId tenantId,
        FirmId? firmId = null,
        BranchId? branchId = null)
    {
        TenantScope? previous = _current.Value;
        _current.Value = new TenantScope(tenantId, firmId, branchId);

        return new ScopeHandle(this, previous);
    }

    private sealed record TenantScope(TenantId TenantId, FirmId? FirmId, BranchId? BranchId);

    /// <summary>
    /// Restores the enclosing scope on disposal, so scopes nest correctly - a job
    /// iterating tenants leaves no residue behind after each one.
    /// </summary>
    private sealed class ScopeHandle : IDisposable
    {
        private readonly AmbientTenantContext _owner;
        private readonly TenantScope? _previous;
        private bool _disposed;

        internal ScopeHandle(AmbientTenantContext owner, TenantScope? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner._current.Value = _previous;
            _disposed = true;
        }
    }
}
