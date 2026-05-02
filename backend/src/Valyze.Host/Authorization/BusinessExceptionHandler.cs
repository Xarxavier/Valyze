using Microsoft.AspNetCore.Diagnostics;
using Valyze.Domain.Exceptions;

namespace Valyze.Host.Authorization;

public sealed class BusinessExceptionHandler : IExceptionHandler
{
    private readonly ILogger<BusinessExceptionHandler> _logger;

    public BusinessExceptionHandler(ILogger<BusinessExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case BusinessException be:
                _logger.LogWarning(be, "Business rule violation: {Message}", be.Message);
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(
                    new { code = be.Message, detail = be.Detail },
                    cancellationToken);
                return true;

            case HandledException he:
                _logger.LogInformation(he, "Handled error: {Message}", he.Message);
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(
                    new { code = he.Message },
                    cancellationToken);
                return true;

            default:
                return false;
        }
    }
}
