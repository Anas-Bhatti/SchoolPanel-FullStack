// ============================================================
// Requirements/PermissionRequirement.cs
// Dynamic permission-based authorization.
// Usage: [Authorize(Policy = "CanManageStudents")]
//        [Authorize(Policy = "Students.Create")]
//
// Two naming conventions supported:
//   Legacy: "CanManage{Module}"  → checks CanCreate+Edit+Delete
//   Modern: "{Module}.{Action}"  → checks specific bit (View/Create/Edit/Delete/Export)
// ============================================================

using System.Data;
using System.Data.SqlClient;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolPanel.Auth.Models;
using SchoolPanel.Auth.Services;

namespace SchoolPanel.Auth.Requirements;

// ─── Requirement ──────────────────────────────────────────────

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Module { get; }
    public string Action { get; }

    // Legacy convenience: "CanManageStudents" → Module=Students, Action=Create
    public static PermissionRequirement Parse(string policyName)
    {
        // Modern format: "Students.Create", "Fees.Export"
        if (policyName.Contains('.'))
        {
            var parts = policyName.Split('.', 2);
            return new PermissionRequirement(parts[0], parts[1]);
        }

        // Legacy format: "CanManageStudents", "CanViewReports"
        if (policyName.StartsWith("CanManage", StringComparison.OrdinalIgnoreCase))
            return new PermissionRequirement(
                policyName["CanManage".Length..], "Manage");

        if (policyName.StartsWith("CanView", StringComparison.OrdinalIgnoreCase))
            return new PermissionRequirement(
                policyName["CanView".Length..], "View");

        if (policyName.StartsWith("CanExport", StringComparison.OrdinalIgnoreCase))
            return new PermissionRequirement(
                policyName["CanExport".Length..], "Export");

        // Fallback: treat entire policy as module with VIEW action
        return new PermissionRequirement(policyName, "View");
    }

    public PermissionRequirement(string module, string action)
    {
        Module = module.Trim();
        Action = action.Trim();
    }
}

// ─── Handler ──────────────────────────────────────────────────

public sealed class PermissionHandler
    : AuthorizationHandler<PermissionRequirement>
{
    private readonly IConfiguration _config;
    private readonly ILogger<PermissionHandler> _logger;

    public PermissionHandler(
        IConfiguration config,
        ILogger<PermissionHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // ── 1. Must be authenticated ──────────────────────────────────────────
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            context.Fail();
            return;
        }

        // ── 2. Reject pending/2FA-gate tokens ────────────────────────────────
        if (context.User.HasClaim(TokenService.ClaimIsPending, "true"))
        {
            _logger.LogWarning(
                "Pending token used on protected endpoint. Module={M}", requirement.Module);
            context.Fail(new AuthorizationFailureReason(this,
                "Two-factor authentication not completed."));
            return;
        }

        // ── 3. SuperAdmin bypasses all checks ─────────────────────────────────
        if (context.User.IsInRole("SuperAdmin"))
        {
            context.Succeed(requirement);
            return;
        }

        // ── 4. Resolve userId ─────────────────────────────────────────────────
        var userIdClaim = context.User.FindFirstValue(TokenService.ClaimUserId)
                       ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            context.Fail();
            return;
        }

        // ── 5. Try JWT permission claims first (fast path) ────────────────────
        //
        // Claims are encoded as "Students:11101" (View|Create|Edit|Delete|Export)
        // This avoids a DB hit on every request for most scenarios.
        var permClaims = context.User.FindAll(TokenService.ClaimPermission).ToList();

        if (permClaims.Count > 0)
        {
            if (EvaluateFromClaims(permClaims, requirement))
            {
                context.Succeed(requirement);
                return;
            }
        }
        else
        {
            // ── 6. Fallback: load from DB (token predates permission claims) ──
            var dbPerms = await LoadPermissionsFromDb(userId);
            if (EvaluateFromDb(dbPerms, requirement))
            {
                context.Succeed(requirement);
                return;
            }
        }

        _logger.LogWarning(
            "Permission denied. UserId={UserId} Module={Module} Action={Action}",
            userId, requirement.Module, requirement.Action);

        context.Fail(new AuthorizationFailureReason(this,
            $"Missing permission: {requirement.Module}.{requirement.Action}"));
    }

    // ─── Evaluate from JWT claims ──────────────────────────────────────────────

    private static bool EvaluateFromClaims(
        List<Claim> claims,
        PermissionRequirement req)
    {
        var matching = claims.Where(c =>
            c.Value.StartsWith(req.Module + ":", StringComparison.OrdinalIgnoreCase));

        foreach (var claim in matching)
        {
            // Format: "Students:11101"
            var parts = claim.Value.Split(':');
            if (parts.Length != 2 || parts[1].Length != 5) continue;

            var mask = parts[1];
            // Positions: 0=View, 1=Create, 2=Edit, 3=Delete, 4=Export

            return req.Action.ToUpperInvariant() switch
            {
                "VIEW" => mask[0] == '1',
                "CREATE" => mask[1] == '1',
                "EDIT" => mask[2] == '1',
                "DELETE" => mask[3] == '1',
                "EXPORT" => mask[4] == '1',
                "MANAGE" => mask[1] == '1' && mask[2] == '1' && mask[3] == '1',
                _ => false
            };
        }

        return false;
    }

    // ─── Evaluate from DB result ───────────────────────────────────────────────

    private static bool EvaluateFromDb(
        IEnumerable<SpPermissionResult> perms,
        PermissionRequirement req)
    {
        var match = perms.FirstOrDefault(p =>
            p.Module.Equals(req.Module, StringComparison.OrdinalIgnoreCase));

        if (match == null) return false;

        return req.Action.ToUpperInvariant() switch
        {
            "VIEW" => match.CanView,
            "CREATE" => match.CanCreate,
            "EDIT" => match.CanEdit,
            "DELETE" => match.CanDelete,
            "EXPORT" => match.CanExport,
            "MANAGE" => match.CanCreate && match.CanEdit && match.CanDelete,
            _ => false
        };
    }

    private async Task<IEnumerable<SpPermissionResult>> LoadPermissionsFromDb(Guid userId)
    {
        try
        {
            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            return await conn.QueryAsync<SpPermissionResult>(
                "dbo.sp_GetUserPermissions",
                new { UserId = userId },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DB permission load failed for UserId={UserId}", userId);
            return Enumerable.Empty<SpPermissionResult>();
        }
    }
}

// ─── Policy catalogue ─────────────────────────────────────────
// Register all policies at startup via AddPermissionPolicies().

public static class Policies
{
    // ── Student module ────────────────────────────────────────
    public const string ViewStudents = "Students.View";
    public const string CreateStudents = "Students.Create";
    public const string EditStudents = "Students.Edit";
    public const string DeleteStudents = "Students.Delete";
    public const string ExportStudents = "Students.Export";
    public const string ManageStudents = "CanManageStudents";

    // ── Teacher module ────────────────────────────────────────
    public const string ViewTeachers = "Teachers.View";
    public const string CreateTeachers = "Teachers.Create";
    public const string EditTeachers = "Teachers.Edit";
    public const string DeleteTeachers = "Teachers.Delete";

    // ── Fee module ────────────────────────────────────────────
    public const string ViewFees = "Fees.View";
    public const string CreateFees = "Fees.Create";
    public const string EditFees = "Fees.Edit";
    public const string ExportFees = "Fees.Export";
    public const string ManageFees = "CanManageFees";

    // ── Exam module ───────────────────────────────────────────
    public const string ViewExams = "Exams.View";
    public const string CreateExams = "Exams.Create";
    public const string EditExams = "Exams.Edit";

    // ── Reports ───────────────────────────────────────────────
    public const string ViewReports = "Reports.View";
    public const string ExportReports = "Reports.Export";

    // ── Roles & Settings ──────────────────────────────────────
    public const string ViewRoles = "Roles.View";
    public const string ManageRoles = "CanManageRoles";
    public const string ViewSettings = "Settings.View";
    public const string EditSettings = "Settings.Edit";

    // ── Audit / Dashboard ─────────────────────────────────────
    public const string ViewAuditLogs = "AuditLogs.View";
    public const string ViewDashboard = "Dashboard.View";

    // ── Notifications ─────────────────────────────────────────
    public const string ViewNotifications = "Notifications.View";
    public const string CreateNotifications = "Notifications.Create";

    // ── All policies as array for bulk registration ────────────
    public static readonly string[] All =
    [
        ViewStudents, CreateStudents, EditStudents, DeleteStudents,
        ExportStudents, ManageStudents,
        ViewTeachers, CreateTeachers, EditTeachers, DeleteTeachers,
        ViewFees, CreateFees, EditFees, ExportFees, ManageFees,
        ViewExams, CreateExams, EditExams,
        ViewReports, ExportReports,
        ViewRoles, ManageRoles,
        ViewSettings, EditSettings,
        ViewAuditLogs, ViewDashboard,
        ViewNotifications, CreateNotifications
    ];
}