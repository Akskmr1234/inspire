using System.Text;
using ERP.Application.Abstractions.Security;
using ERP.Identity.Authorization;
using ERP.Identity.Passwords;
using ERP.Identity.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Identity;

/// <summary>Registers authentication and authorisation.</summary>
public static class DependencyInjection
{
    /// <summary>Adds JWT authentication and the permission-based authorisation stack.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddErpIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ValidateOnStart turns a missing or too-short signing key into a startup
        // failure with a clear message, rather than a puzzling 401 on the first
        // sign-in attempt.
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher>(_ => new Pbkdf2PasswordHasher());
        services.AddSingleton<ITokenService, JwtTokenService>();

        JwtOptions options = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,

                    // The library default is five minutes, which silently extends
                    // every token's life by that much. Thirty seconds still absorbs
                    // ordinary clock drift between servers.
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),

                    // Without this the handler rewrites "sub" to a long WS-* URI,
                    // and the tenant middleware stops finding the subject.
                    NameClaimType = "sub",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };

                bearer.MapInboundClaims = false;
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        return services;
    }
}
