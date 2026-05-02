namespace Valyze.Host.MinimalApi.Health;

public static class HealthEndpoints
{
    public static RouteGroupBuilder MapHealthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/health").WithTags("Health");

        group.MapGet("/", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous()
            .WithName("Health");

        return group;
    }
}
