using ERP.Application.Abstractions.Tenancy;
using ERP.SharedKernel.Tenancy;

namespace ERP.Infrastructure.Tenancy;

/// <summary>
/// The default <see cref="ICurrentUser"/>, holding the acting user in an
/// <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// Populated by the API layer from the authenticated principal, and by background
/// work explicitly. Holds no reference to <c>HttpContext</c> so the same
/// implementation serves requests, Hangfire jobs, and tests.
/// </remarks>
public sealed class AmbientCurrentUser : ICurrentUser
{
    // An instance field rather than a static one. The service is registered as a
    // singleton, so the behaviour is identical, but nothing here is global mutable
    // state: a test can construct its own instance without leaking into the next
    // test, and the lifetime is decided by the container rather than by the CLR.
    private readonly AsyncLocal<ActingUser?> _current = new();

    /// <inheritdoc />
    /// <remarks>
    /// Falls back to <see cref="UserId.System"/> rather than throwing. Every write
    /// records an actor, and platform-initiated work genuinely has no person
    /// behind it - a recognisable sentinel is more useful in an audit trail than a
    /// failed save.
    /// </remarks>
    public UserId UserId => _current.Value?.UserId ?? UserId.System;

    /// <inheritdoc />
    public string? UserName => _current.Value?.UserName;

    /// <inheritdoc />
    public bool IsAuthenticated => _current.Value is not null;

    /// <summary>Establishes the acting user for the current flow of control.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="userName">The user's display name.</param>
    /// <returns>A handle restoring the previous user on disposal.</returns>
    public IDisposable BeginScope(UserId userId, string? userName = null)
    {
        ActingUser? previous = _current.Value;
        _current.Value = new ActingUser(userId, userName);

        return new ScopeHandle(this, previous);
    }

    private sealed record ActingUser(UserId UserId, string? UserName);

    private sealed class ScopeHandle : IDisposable
    {
        private readonly AmbientCurrentUser _owner;
        private readonly ActingUser? _previous;
        private bool _disposed;

        internal ScopeHandle(AmbientCurrentUser owner, ActingUser? previous)
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
