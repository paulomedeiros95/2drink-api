using Microsoft.AspNetCore.Diagnostics;
using TwoDrink.Domain.Errors;

namespace TwoDrink.Api.Errors;

/// <summary>Maps a <see cref="RuleViolation"/> to ProblemDetails (422) carrying the snake_case <c>code</c> extension.</summary>
public class RuleViolationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not RuleViolation violation)
            return false;

        http.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails =
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Rule violation",
                Detail = violation.Message,
                Extensions = { ["code"] = violation.Code },
            },
        });
    }
}
