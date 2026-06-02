using System.Text.Json;
using NovaTechCRM.Domain.Exceptions;

namespace NovaTechCRM.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ctx, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext ctx, Exception ex)
    {
        var (status, code, message) = ex switch
        {
            DomainException d   => (400, "DOMAIN_ERROR",    d.Message),
            UnauthorizedAccessException => (401, "UNAUTHORIZED", "Authentication required."),
            KeyNotFoundException k      => (404, "NOT_FOUND",    k.Message),
            InvalidOperationException i => (422, "INVALID_OP",   i.Message),
            _                           => (500, "INTERNAL_ERROR", "An unexpected error occurred.")
        };

        if (status >= 500)
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);
        else
            _logger.LogWarning(ex, "Handled exception {Code} on {Path}", code, ctx.Request.Path);

        ctx.Response.StatusCode  = status;
        ctx.Response.ContentType = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            error   = code,
            message,
            traceId = ctx.TraceIdentifier,
        }, _json);

        await ctx.Response.WriteAsync(body);
    }
}
