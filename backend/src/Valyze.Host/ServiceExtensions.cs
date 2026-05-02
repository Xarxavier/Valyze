using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Valyze.Domain.Application.Auth;
using Valyze.Domain.Entities.Identity;
using Valyze.Host.Authorization;
using Valyze.Host.Configuration;
using Valyze.Host.Setup;

namespace Valyze.Host;

public static class ServiceExtensions
{
    public const string CorsPolicy = "valyze-clients";

    public static IServiceCollection AddValyzeHost(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<ValyzeOptions>()
            .Bind(configuration.GetSection(ValyzeOptions.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(j => !string.IsNullOrWhiteSpace(j.SigningKey), "Jwt:SigningKey is required.")
            .ValidateOnStart();

        services.AddScoped<AccessorClassEntity>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<SeedRunner>();
        services.AddExceptionHandler<BusinessExceptionHandler>();
        services.AddProblemDetails();

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var signingKey = jwtSection["SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidAudience = jwtSection["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, policy =>
            {
                var origins = configuration
                    .GetSection($"{ValyzeOptions.SectionName}:Cors:AllowedOrigins")
                    .Get<string[]>() ?? [];
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        services.AddEndpointsApiExplorer();
        services.AddOpenApi();

        return services;
    }
}
