using System.Security.Claims;
using Valyze.Domain.Entities.Identity;

namespace Valyze.Host.Authorization;

public sealed class AccessorClassMiddleware
{
    private readonly RequestDelegate _next;

    public AccessorClassMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AccessorClassEntity accessor)
    {
        var accountClaim = context.User.FindFirst(JwtTokenService.AccountIdClaim)
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier);

        if (accountClaim is not null && Guid.TryParse(accountClaim.Value, out var accountId))
        {
            accessor.AccountId = accountId;
        }

        accessor.Email = context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value;

        await _next(context);
    }
}
