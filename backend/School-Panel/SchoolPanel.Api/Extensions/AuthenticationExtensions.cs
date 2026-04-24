// ============================================================
// Extensions/AuthenticationExtensions.cs
// Wires: JWT Bearer (HS512), Google external token support,
//        all permission policies, DI registrations
// ============================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SchoolPanel.Auth.Requirements;
using SchoolPanel.Auth.Services;
using System.Management;
using System.Text;

namespace SchoolPanel.Auth.Extensions;

public static class AuthenticationExtensions
{
    private static readonly Action<ILogger, string, Exception?> LogJwtExpired =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, "JwtExpired"),
            "JWT expired. Path={Path}");

    private static readonly Action<ILogger, string, Exception?> LogJwtAuthenticationFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, "JwtAuthenticationFailed"),
            "JWT authentication failed. Path={Path}");
    // ─────────────────────────────────────────────────────────────────────────
    // AddSchoolPanelAuth — single call registers everything
    // ─────────────────────────────────────────────────────────────────────────
    public static IServiceCollection AddSchoolPanelAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── Options ───────────────────────────────────────────────────────────
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<TotpOptions>(config.GetSection(TotpOptions.Section));
        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));

        // ── Core services ─────────────────────────────────────────────────────
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IAuthDataService, AuthDataService>();
        services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();

        // ── Authorization handler (scoped — needs DB per request) ─────────────
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        // ── JWT Bearer ────────────────────────────────────────────────────────
        services.AddJwtBearer(config);

        // ── Permission policies ───────────────────────────────────────────────
        services.AddPermissionPolicies();

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JWT Bearer configuration
    // ─────────────────────────────────────────────────────────────────────────
    private static IServiceCollection AddJwtBearer(
        this IServiceCollection services,
        IConfiguration config)
    {
        var jwtSection = config.GetSection(JwtOptions.Section);
        var secretKey = jwtSection["SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey is required.");

        if (secretKey.Length < 64)
            throw new InvalidOperationException(
                "Jwt:SecretKey must be ≥64 characters for HS512.");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Never redirect — this is a pure API
                options.RequireHttpsMetadata = false;    // Enforced by HTTPS middleware
                options.SaveToken = false;    // We manage tokens ourselves

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Signature
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    // Issuer / Audience
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"] ?? "schoolpanel",
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"] ?? "schoolpanel-api",

                    // Lifetime
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    RequireExpirationTime = true,

                    // Enforce HS512 only — reject weaker algorithms
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha512],

                    // Map sub → NameIdentifier automatically
                    NameClaimType = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
                    RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
                };

                // ── Events — return JSON, never HTML ──────────────────────────
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILogger<JwtBearerEvents>>();

                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.Headers.Append(
                                "X-Token-Expired", "true");
                            LogJwtExpired(logger, context.Request.Path.Value ?? "", null);
                        }
                        else
                        {
                            LogJwtAuthenticationFailed(logger, context.Request.Path.Value ?? "", context.Exception);
                        }

                        return Task.CompletedTask;
                    },

                    OnChallenge = async context =>
                    {
                        // Suppress default redirect/401 challenge
                        context.HandleResponse();

                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/problem+json";

                        var expired = context.AuthenticateFailure
                            is SecurityTokenExpiredException;

                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = 401,
                            code = expired ? "TOKEN_EXPIRED" : "UNAUTHORIZED",
                            message = expired
                                ? "Access token has expired. Please refresh."
                                : "Authentication is required.",
                            instance = context.Request.Path.Value
                        });
                    },

                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/problem+json";

                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = 403,
                            code = "FORBIDDEN",
                            message = "You do not have permission to access this resource.",
                            instance = context.Request.Path.Value
                        });
                    }
                };
            });

        return services;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Permission Policies — register every entry from Policies.All
    // ─────────────────────────────────────────────────────────────────────────
    private static IServiceCollection AddPermissionPolicies(
        this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Default — any authenticated user, no 2FA-pending tokens
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new NoPendingTokenRequirement())
                .Build();

            // SuperAdmin only
            options.AddPolicy("SuperAdminOnly",
                p => p.RequireRole("SuperAdmin"));

            // All granular permission policies
            foreach (var policyName in Policies.All)
            {
                var req = PermissionRequirement.Parse(policyName);
                options.AddPolicy(policyName,
                    p => p.Requirements.Add(req));
            }
        });

        // Register the no-pending-token handler
        services.AddScoped<IAuthorizationHandler, NoPendingTokenHandler>();

        return services;
    }
}

// ─── NoPendingToken requirement — blocks 2FA-gate tokens ──────

/// <summary>
/// Prevents tokens issued only for the 2FA step from being used
/// on any real endpoint. Added to the default policy.
/// </summary>
public sealed class NoPendingTokenRequirement : IAuthorizationRequirement { }

public sealed class NoPendingTokenHandler
    : AuthorizationHandler<NoPendingTokenRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        NoPendingTokenRequirement requirement)
    {
        if (context.User.HasClaim(TokenService.ClaimIsPending, "true"))
        {
            context.Fail(new AuthorizationFailureReason(this,
                "Pending 2FA token cannot access this endpoint."));
        }
        else
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// ─── SecurityOptions ──────────────────────────────────────────

public sealed class SecurityOptions
{
    public const string Section = "Security";

    public int MaxLoginAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public int BcryptWorkFactor { get; init; } = 12;
    public bool RequireHttps { get; init; } = true;
}