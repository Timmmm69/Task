using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Task.Api.Security;

internal static class TaskApiProblemResponse
{
    public const string CorrelationIdItemName = "Task.Api.CorrelationId";

    public const string AuthenticationResponseWrittenItemName = "Task.Api.Authentication.ResponseWritten";

    public static global::System.Threading.Tasks.Task WriteAsync(
        HttpContext context,
        int statusCode,
        string code,
        string title,
        bool retryable)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        context.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://task.local/errors/{code.ToLowerInvariant()}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = GetCorrelationId(context);
        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["retryable"] = retryable;

        context.Response.ContentType = "application/problem+json";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            cancellationToken: context.RequestAborted);
    }

    public static string GetCorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdItemName, out var value) && value is string correlationId
            ? correlationId
            : Guid.NewGuid().ToString("D");
}

internal sealed class TaskApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<TaskApiExceptionHandler> _logger;

    public TaskApiExceptionHandler(ILogger<TaskApiExceptionHandler> logger)
    {
        _logger = logger;
    }

    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            "Unhandled API exception. Correlation ID: {CorrelationId}; trace ID: {TraceId}",
            TaskApiProblemResponse.GetCorrelationId(httpContext),
            httpContext.TraceIdentifier);

        return HandleAsync(httpContext);
    }

    private static async ValueTask<bool> HandleAsync(HttpContext httpContext)
    {
        await TaskApiProblemResponse.WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            code: "INTERNAL_ERROR",
            title: "An unexpected server error occurred.",
            retryable: true);
        return true;
    }
}
