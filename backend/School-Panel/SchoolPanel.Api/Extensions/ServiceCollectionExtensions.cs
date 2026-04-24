using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using SchoolPanel.Api.Configuration;
using SchoolPanel.Api.Filters;
using SchoolPanel.Api.Repositories;
using SchoolPanel.Api.Services;
using SchoolPanel.Auth.Requirements;
using SchoolPanel.Auth.Services;
using System.Threading.RateLimiting;

using JwtOptions = SchoolPanel.Api.Configuration.JwtOptions;
using GoogleOptions = SchoolPanel.Api.Configuration.GoogleOptions;
using PermissionHandler = SchoolPanel.Api.Filters.PermissionHandler;
using PermissionRequirement = SchoolPanel.Api.Filters.PermissionRequirement;

namespace SchoolPanel.Api.Extensions;

public static class ServiceCollectionExtensions
{
    // ─── Options ──────────────────────────────────────────────────────────────
    public static IServiceCollection AddAppOptions(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<GoogleOptions>(config.GetSection(GoogleOptions.Section));
        services.Configure<TwoFactorOptions>(config.GetSection(TwoFactorOptions.Section));
        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
        services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.Section));
        services.Configure<CorsOptions>(config.GetSection(CorsOptions.Section));
        services.Configure<AzureBlobOptions>(config.GetSection(AzureBlobOptions.Section));
        return services;
    }

    // ─── Repositories ─────────────────────────────────────────────────────────
    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<ITeacherRepository, TeacherRepository>();
        services.AddScoped<IFeeRepository, FeeRepository>();
        services.AddScoped<IExamRepository, ExamRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IRolesRepository, RolesRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        return services;
    }

    // ─── Application Services ─────────────────────────────────────────────────
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IFileUploadService, FileUploadService>();

        // HttpClient for Google OAuth — pooled, with retry-friendly defaults
        services.AddHttpClient<IGoogleOAuthService, GoogleOAuthService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        return services;
    }

    // ─── Authorization Policies ───────────────────────────────────────────────
    public static IServiceCollection AddPermissionAuthorization(
        this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        services.AddAuthorization(options =>
        {
            // Register every granular permission policy
            foreach (var (policy, module, action) in PermissionPolicies.All)
            {
                options.AddPolicy(policy,
                    p => p.Requirements.Add(new PermissionRequirement(module, action)));
            }

            // Convenience: require any authenticated user
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            // SuperAdmin-only policy
            options.AddPolicy("SuperAdminOnly",
                p => p.RequireRole("SuperAdmin"));
        });

        return services;
    }

    // ─── Rate Limiting ────────────────────────────────────────────────────────
    public static IServiceCollection AddAppRateLimiting(
        this IServiceCollection services,
        IConfiguration config)
    {
        var rl = config.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>()
              ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // ── Per-IP global limiter ──────────────────────────────────────────
            options.AddPolicy("PerIp", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rl.PerIp.PermitLimit,
                        Window = TimeSpan.FromSeconds(rl.PerIp.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            // ── Per-User limiter (authenticated routes) ────────────────────────
            options.AddPolicy("PerUser", context =>
            {
                var userId = context.User.FindFirst("sub")?.Value
                          ?? context.Connection.RemoteIpAddress?.ToString()
                          ?? "anon";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rl.PerUser.PermitLimit,
                        Window = TimeSpan.FromSeconds(rl.PerUser.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // ── Auth endpoint limiter (stricter — prevents brute force) ─────────
            options.AddPolicy("AuthLimit", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rl.AuthEndpoint.PermitLimit,
                        Window = TimeSpan.FromSeconds(rl.AuthEndpoint.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    // ─── CORS ─────────────────────────────────────────────────────────────────
    public static IServiceCollection AddAppCors(
        this IServiceCollection services,
        IConfiguration config)
    {
        var corsConfig = config.GetSection(CorsOptions.Section).Get<CorsOptions>()
                      ?? new CorsOptions();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsOptions.PolicyName, policy =>
            {
                policy.WithOrigins(corsConfig.AllowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()   // Required for cookie/refresh token flows
                      .WithExposedHeaders("X-Pagination"); // For paginated responses
            });
        });

        return services;
    }

    // ─── Swagger ──────────────────────────────────────────────────────────────
    public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "School Management Panel API",
                Version = "v1",
                Description = "Production-grade school management REST API"
            });

            // Handle duplicate DTO names across namespaces
            options.CustomSchemaIds(type => type.FullName);

            // JWT Bearer security definition
            options.AddSecurityDefinition("Bearer", new()
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "Enter: Bearer {your JWT access token}"
            });

            options.AddSecurityRequirement(new()
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new()
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id   = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}