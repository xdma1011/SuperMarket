using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.API.Common;

/// <summary>
/// Translates the Application layer's Result/Error model into HTTP.
///
/// This is the ONLY place in the solution that knows the mapping from a
/// business failure to a status code — handlers return ErrorType, never
/// IResult, which is what keeps the Application layer usable from a
/// future background job or desktop client without dragging HTTP along.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult>? onSuccess = null)
    {
        if (result.IsSuccess)
        {
            return onSuccess is not null ? onSuccess(result.Value) : Results.Ok(result.Value);
        }

        return Problem(result.Error!);
    }

    public static IResult ToHttpResult(this Result result)
        => result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    private static IResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            // 409 for both Conflict and Concurrency: from the client's point
            // of view an optimistic-concurrency loss IS a conflict, and the
            // correct client behaviour (re-read, retry) is identical.
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Concurrency => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            // 422, not 400: the request was syntactically valid, but a
            // business rule rejected it (insufficient stock, over-payment).
            ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(
            title: error.Code,
            detail: error.Message,
            statusCode: statusCode);
    }
}

/// <summary>
/// Catches what Result cannot: genuinely unexpected failures, plus the two
/// database-level races that the Application layer deliberately does not
/// pre-check away (unique violations and concurrency conflicts).
///
/// Those two are translated to 409 rather than 500 because they are not
/// bugs — they are the database correctly enforcing an invariant under
/// concurrency, which is exactly what it is there for.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    // SQL Server error numbers for unique index/constraint violations.
    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Optimistic concurrency conflict on {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "Concurrency.Conflict",
                "The record was modified by another user. Reload and try again.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Unique constraint violation on {Path}", context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "Conflict.DuplicateValue",
                "A record with the same unique value already exists.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
            // Deliberately no exception detail in the response body — that
            // would leak schema/internals. The correlation id in the response
            // header is how this response is tied back to the logged error.
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Server.Error",
                "An unexpected error occurred.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
           && sqlEx.Number is SqlUniqueIndexViolation or SqlUniqueConstraintViolation;

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        });
    }
}

/// <summary>
/// Assigns a correlation id to every request and echoes it back, so a single
/// operation can be traced API → Application → Infrastructure → Database
/// through the logs (brief §27). Accepts a client-supplied id when present so
/// a trace can span a multi-request client workflow.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var supplied)
                            && !string.IsNullOrWhiteSpace(supplied)
            ? supplied.ToString()
            : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        // Scoped logging: every log line written downstream carries the id.
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
