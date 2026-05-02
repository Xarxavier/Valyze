using Microsoft.Extensions.Options;
using Valyze.Domain.Application.Auth;
using Valyze.Domain.Enum;
using Valyze.Host.Configuration;

namespace Valyze.Host.MinimalApi.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/dev-login", async (
            IDevLoginUseCase useCase,
            IOptions<ValyzeOptions> options,
            CancellationToken ct) =>
        {
            if (options.Value.Mode != ValyzeMode.Personal)
                return Results.NotFound();

            var response = await useCase.ExecuteAsync(ct);
            return Results.Ok(response);
        }).WithName("DevLogin");

        return group;
    }
}
