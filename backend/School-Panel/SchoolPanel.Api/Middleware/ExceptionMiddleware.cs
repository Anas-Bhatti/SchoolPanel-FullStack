using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SchoolPanel.Api.Middleware;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled exception. Path={Path} Method={Method} TraceId={TraceId}",
                context.Request.Path,
                context.Request.Method,
                context.TraceIdentifier);

            await WriteErrorResponseAsync(context, ex);
        }
    }

    private async Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title) = ex switch
        {
            ArgumentNullException => (400, "Bad Request"),
            ArgumentException => (400, "Bad Request"),
            UnauthorizedAccessException => (401, "Unauthorized"),
            KeyNotFoundException => (404, "Not Found"),
            InvalidOperationException => (409, "Conflict"),
            OperationCanceledException => (499, "Client Closed Request"),
            _ => (500, "Internal Server Error")
        };

        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = _env.IsDevelopment() ? ex.Message : "An error occurred.",
            Instance = context.Request.Path,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier,
                ["timestamp"] = DateTime.UtcNow
            }
        };

        if (_env.IsDevelopment())
            problem.Extensions["stackTrace"] = ex.StackTrace;

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}