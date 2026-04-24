using Dapper;
using Microsoft.AspNetCore.Authorization;
using SchoolPanel.Api.DTOs;
using SchoolPanel.Auth.DTOs;
using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;

namespace SchoolPanel.Api.Filters;

// ─── Requirement ──────────────────────────────────────────────────────────────

/// <summary>
/// Marks a route as requiring a specific permission on a module.
/// Usage: [Authorize(Policy = "Students.CanCreate")]
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Module { get; }
    public string Action { get; }   // View | Create | Edit | Delete | Export

    public PermissionRequirement(string module, string action)
    {
        Module = module;
        Action = action;
    }
}

// ─── Handler ──────────────────────────────────────────────────────────────────

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IConfiguration _config;
    private readonly ILogger<PermissionHandler> _logger;

    public PermissionHandler(IConfiguration config, ILogger<PermissionHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? context.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            context.Fail();
            return;
        }

        // SuperAdmin bypasses all permission checks
        if (context.User.IsInRole("SuperAdmin"))
        {
            context.Succeed(requirement);
            return;
        }

        // Load permissions from DB for this user's roles
        var permissions = await LoadPermissionsAsync(userId);

        var permission = permissions.FirstOrDefault(p =>
            p.Module.Equals(requirement.Module, StringComparison.OrdinalIgnoreCase));

        if (permission == null)
        {
            context.Fail();
            return;
        }

        var allowed = requirement.Action.ToUpperInvariant() switch
        {
            "VIEW" => permission.CanView,
            "CREATE" => permission.CanCreate,
            "EDIT" => permission.CanEdit,
            "DELETE" => permission.CanDelete,
            "EXPORT" => permission.CanExport,
            _ => false
        };

        if (allowed)
            context.Succeed(requirement);
        else
        {
            _logger.LogWarning(
                "Permission denied. UserId={UserId} Module={Module} Action={Action}",
                userId, requirement.Module, requirement.Action);
            context.Fail();
        }
    }

    private async Task<IEnumerable<Permission>> LoadPermissionsAsync(Guid userId)
    {
        var cs = _config.GetConnectionString("DefaultConnection")!;
        using var conn = new SqlConnection(cs);

        return await conn.QueryAsync<Permission>(
            """
            SELECT rp.Module, rp.CanView, rp.CanCreate,
                   rp.CanEdit, rp.CanDelete, rp.CanExport
            FROM   dbo.UserRoles ur
            JOIN   dbo.RolePermissions rp ON ur.RoleId = rp.RoleId
            WHERE  ur.UserId = @UserId
            """,
            new { UserId = userId });
    }
}

// ─── Policy Registration Helper ───────────────────────────────────────────────

public static class PermissionPolicies
{
    // Format: "{Module}.Can{Action}"
    public static readonly (string Policy, string Module, string Action)[] All =
    [
        // Students
        ("Students.View",   "Students", "VIEW"),
        ("Students.Create", "Students", "CREATE"),
        ("Students.Edit",   "Students", "EDIT"),
        ("Students.Delete", "Students", "DELETE"),
        ("Students.Export", "Students", "EXPORT"),
        // Teachers
        ("Teachers.View",   "Teachers", "VIEW"),
        ("Teachers.Create", "Teachers", "CREATE"),
        ("Teachers.Edit",   "Teachers", "EDIT"),
        ("Teachers.Delete", "Teachers", "DELETE"),
        // Fees
        ("Fees.View",       "Fees",     "VIEW"),
        ("Fees.Create",     "Fees",     "CREATE"),
        ("Fees.Edit",       "Fees",     "EDIT"),
        ("Fees.Export",     "Fees",     "EXPORT"),
        // Exams
        ("Exams.View",      "Exams",    "VIEW"),
        ("Exams.Create",    "Exams",    "CREATE"),
        ("Exams.Edit",      "Exams",    "EDIT"),
        // Reports
        ("Reports.View",    "Reports",  "VIEW"),
        ("Reports.Export",  "Reports",  "EXPORT"),
        // Roles
        ("Roles.View",      "Roles",    "VIEW"),
        ("Roles.Create",    "Roles",    "CREATE"),
        ("Roles.Edit",      "Roles",    "EDIT"),
        ("Roles.Delete",    "Roles",    "DELETE"),
        // Settings
        ("Settings.View",   "Settings", "VIEW"),
        ("Settings.Edit",   "Settings", "EDIT"),
        // AuditLogs
        ("AuditLogs.View",  "AuditLogs","VIEW"),
        // Dashboard
        ("Dashboard.View",  "Dashboard","VIEW"),
        // Notifications
        ("Notifications.View",   "Notifications", "VIEW"),
        ("Notifications.Create", "Notifications", "CREATE"),
    ];
}