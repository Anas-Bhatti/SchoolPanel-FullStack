// ============================================================
// Controllers/AppControllerBase.cs
// Base class providing shared helpers: UserId extraction,
// IP resolution, ProblemDetails shortcuts, audit helpers.
// All three feature controllers extend this.
// ============================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SchoolPanel.Controllers.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class AppControllerBase : ControllerBase
{
    // ─── Identity helpers ─────────────────────────────────────

    protected Guid? CurrentUserId
    {
        get
        {
            var claim = User.FindFirstValue("uid")
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue("sub");
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    protected string? CurrentEmail
        => User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("email");

    protected string ClientIp
    {
        get
        {
            var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            return !string.IsNullOrEmpty(fwd)
                ? fwd.Split(',')[0].Trim()
                : HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }

    protected string UserAgent
        => Request.Headers["User-Agent"].ToString();

    // ─── ProblemDetails responses ─────────────────────────────

    protected IActionResult NotFoundProblem(string detail)
        => Problem(statusCode: 404, title: "Not Found",
            detail: detail, extensions: new() { ["code"] = "NOT_FOUND" });

    protected IActionResult BadRequestProblem(string detail)
        => Problem(statusCode: 400, title: "Bad Request",
            detail: detail, extensions: new() { ["code"] = "BAD_REQUEST" });

    protected IActionResult ConflictProblem(string detail)
        => Problem(statusCode: 409, title: "Conflict",
            detail: detail, extensions: new() { ["code"] = "CONFLICT" });

    protected IActionResult UnprocessableProblem(string detail)
        => Problem(statusCode: 422, title: "Unprocessable Entity",
            detail: detail, extensions: new() { ["code"] = "UNPROCESSABLE" });

    protected IActionResult ServerErrorProblem(string detail = "An unexpected error occurred.")
        => Problem(statusCode: 500, title: "Server Error",
            detail: detail, extensions: new() { ["code"] = "SERVER_ERROR" });

    // ─── Pagination header ────────────────────────────────────

    protected void SetPaginationHeader(int totalCount, int page, int pageSize)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        Response.Headers.Append("X-Pagination",
            $"page={page},size={pageSize},total={totalCount},pages={totalPages}");
        Response.Headers.Append("Access-Control-Expose-Headers", "X-Pagination");
    }

    // ─── Private Problem helper ───────────────────────────────

    private IActionResult Problem(int statusCode, string title, string detail,
        Dictionary<string, object?> extensions)
    {
        var pd = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = Request.Path
        };
        foreach (var (k, v) in extensions)
            pd.Extensions[k] = v;
        return new ObjectResult(pd) { StatusCode = statusCode };
    }
}