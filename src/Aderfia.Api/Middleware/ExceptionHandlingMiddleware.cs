using System.Text.Json;
using Aderfia.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Aderfia.Api.Middleware;

/// <summary>
/// Translates domain and application exceptions into RFC 7807 problem
/// details.
/// <para>
/// Having this in one place is what lets use cases simply throw
/// <see cref="NotFoundException"/> instead of returning HTTP-shaped results,
/// and guarantees an unexpected error never leaks a stack trace to a client.
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        // A cancelled request is the client hanging up, not a server fault.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by the client: {Path}", context.Request.Path);
            return;
        }

        var (status, title, detail, code) = exception switch
        {
            NotFoundException => (
                StatusCodes.Status404NotFound,
                "Not found",
                exception.Message,
                "not_found"),

            BusinessRuleException { IsConflict: true } => (
                StatusCodes.Status409Conflict,
                "Conflict",
                exception.Message,
                "conflict"),

            BusinessRuleException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message,
                "invalid_request"),

            ArgumentException or FormatException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                exception.Message,
                "invalid_request"),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Something went wrong",
                // Never surface internal detail to a client in production.
                environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred. Please try again.",
                "server_error")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("{Code} on {Method} {Path}: {Message}",
                code, context.Request.Method, context.Request.Path, exception.Message);

        // Headers already sent — the response is committed and cannot be rewritten.
        if (context.Response.HasStarted)
        {
            logger.LogWarning("Response already started; problem details could not be written.");
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = code,
            Instance = context.Request.Path
        };

        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
