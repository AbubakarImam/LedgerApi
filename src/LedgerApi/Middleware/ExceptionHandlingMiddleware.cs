using LedgerApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request cancelled by client: {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, title) = Map(ex);
            var isExpected = status != StatusCodes.Status500InternalServerError;

            if (isExpected)
                logger.LogWarning(ex, "Request rejected ({Status}) {Method} {Path}", status, context.Request.Method, context.Request.Path);
            else
                logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                // Only expected business errors expose their message; unexpected ones must not leak internals.
                Detail = isExpected ? ex.Message : null
            });
        }
    }

    private static (int Status, string Title) Map(Exception ex) => ex switch
    {
        AccountNotFoundException => (StatusCodes.Status404NotFound, "Account not found."),
        TransactionNotFoundException => (StatusCodes.Status404NotFound, "Transaction not found."),
        TransactionNotReversibleException => (StatusCodes.Status422UnprocessableEntity, "Transaction cannot be reversed."),
        DuplicateIdempotencyKeyException => (StatusCodes.Status409Conflict, "Duplicate idempotency key."),
        AlreadyReversedException => (StatusCodes.Status409Conflict, "Transaction has already been reversed."),
        InsufficientFundsException => (StatusCodes.Status422UnprocessableEntity, "Insufficient funds."),
        CurrencyMismatchException => (StatusCodes.Status422UnprocessableEntity, "Currency mismatch."),
        InvalidAccountStatusException => (StatusCodes.Status422UnprocessableEntity, "Account status does not allow this operation."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };
}
