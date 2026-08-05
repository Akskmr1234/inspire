using System.Security.Claims;
using ERP.Infrastructure.Tenancy;
using ERP.SharedKernel.Tenancy;

namespace ERP.Api.Middleware;

/// <summary>
/// Establishes the tenant, firm, branch, and acting user for the request from
/// the authenticated principal.
/// </summary>
/// <remarks>
/// <para>
/// The bridge between HTTP and the rest of the application. Everything
/// downstream - query filters, the row-level-security session variable, audit
/// stamps - reads from the ambient contexts this populates, and none of it knows
/// that HTTP exists.
/// </para>
/// <para>
/// Ordering matters: this must run <em>after</em> authentication, or there is no
/// principal to read, and <em>before</em> anything that touches the database, or a
/// query could run with no tenant set.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware
{
    /// <summary>The claim carrying the tenant.</summary>
    public const string TenantClaim = "tenant_id";

    /// <summary>The claim carrying the selected firm.</summary>
    public const string FirmClaim = "firm_id";

    /// <summary>The claim carrying the selected branch.</summary>
    public const string BranchClaim = "branch_id";

    /// <summary>
    /// The header a client sends to switch firm without re-issuing its token.
    /// </summary>
    /// <remarks>
    /// A user may have access to several firms and switches between them in
    /// session. The header is only ever honoured when the token's claims permit
    /// that firm - see <see cref="ResolveFirm"/>.
    /// </remarks>
    public const string FirmHeader = "X-Erp-Firm";

    /// <summary>The header a client sends to switch branch.</summary>
    public const string BranchHeader = "X-Erp-Branch";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="TenantResolutionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger.</param>
    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="tenantContext">The ambient tenant scope to populate.</param>
    /// <param name="currentUser">The ambient user scope to populate.</param>
    /// <returns>A task representing the request.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        AmbientTenantContext tenantContext,
        AmbientCurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(currentUser);

        ClaimsPrincipal principal = context.User;

        // Anonymous requests - the health endpoints, the token endpoint, Swagger -
        // proceed with no tenant. They must not touch tenant-scoped data, and if
        // they try, both isolation layers return nothing rather than everything.
        if (principal.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        if (!TryReadGuidClaim(principal, TenantClaim, out Guid tenantId))
        {
            // An authenticated token with no tenant is a misconfigured client or a
            // misconfigured realm. Refusing is safer than continuing untenanted and
            // letting it fail obscurely at the first query.
            _logger.LogWarning(
                "Authenticated request from {User} carried no {Claim} claim",
                principal.Identity?.Name,
                TenantClaim);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new
                {
                    type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                    title = "Tenant not resolved",
                    status = StatusCodes.Status403Forbidden,
                    detail = "The access token does not identify a tenant.",
                },
                context.RequestAborted);

            return;
        }

        FirmId? firmId = ResolveFirm(context, principal);
        BranchId? branchId = ResolveBranch(context, principal);

        using IDisposable tenantScope = tenantContext.BeginScope(
            TenantId.From(tenantId), firmId, branchId);

        using IDisposable userScope = currentUser.BeginScope(
            ResolveUserId(principal),
            principal.Identity?.Name);

        await _next(context);
    }

    /// <summary>Resolves the firm in scope, honouring the switch header when permitted.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The firm, if one is in scope.</returns>
    /// <remarks>
    /// The header is a convenience, not an authority. A requested firm is accepted
    /// only if the token already grants access to it; otherwise the token's own
    /// firm claim stands. Trusting the header outright would let any client read
    /// any firm by editing a request.
    /// </remarks>
    private static FirmId? ResolveFirm(HttpContext context, ClaimsPrincipal principal)
    {
        HashSet<Guid> permitted = ReadGuidClaims(principal, FirmClaim);

        if (context.Request.Headers.TryGetValue(FirmHeader, out var requested)
            && Guid.TryParse(requested.ToString(), out Guid requestedFirm)
            && permitted.Contains(requestedFirm))
        {
            return FirmId.From(requestedFirm);
        }

        return permitted.Count > 0 ? FirmId.From(permitted.First()) : null;
    }

    /// <summary>Resolves the branch in scope, honouring the switch header when permitted.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The branch, if one is in scope.</returns>
    private static BranchId? ResolveBranch(HttpContext context, ClaimsPrincipal principal)
    {
        HashSet<Guid> permitted = ReadGuidClaims(principal, BranchClaim);

        if (context.Request.Headers.TryGetValue(BranchHeader, out var requested)
            && Guid.TryParse(requested.ToString(), out Guid requestedBranch)
            && permitted.Contains(requestedBranch))
        {
            return BranchId.From(requestedBranch);
        }

        return permitted.Count > 0 ? BranchId.From(permitted.First()) : null;
    }

    private static UserId ResolveUserId(ClaimsPrincipal principal) =>
        TryReadGuidClaim(principal, ClaimTypes.NameIdentifier, out Guid id)
            || TryReadGuidClaim(principal, "sub", out id)
                ? UserId.From(id)
                : UserId.System;

    private static bool TryReadGuidClaim(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value)
    {
        string? raw = principal.FindFirstValue(claimType);
        return Guid.TryParse(raw, out value);
    }

    private static HashSet<Guid> ReadGuidClaims(ClaimsPrincipal principal, string claimType)
    {
        HashSet<Guid> values = [];

        foreach (Claim claim in principal.FindAll(claimType))
        {
            if (Guid.TryParse(claim.Value, out Guid parsed))
            {
                values.Add(parsed);
            }
        }

        return values;
    }
}

/// <summary>Registers <see cref="TenantResolutionMiddleware"/>.</summary>
public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>
    /// Adds tenant resolution to the pipeline. Must be placed after
    /// <c>UseAuthentication</c> and before any endpoint that reads data.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder, for chaining.</returns>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
