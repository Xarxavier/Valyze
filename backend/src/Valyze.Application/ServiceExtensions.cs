using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Valyze.Application.Auth;
using Valyze.Application.Decisions;
using Valyze.Application.Ingestion;
using Valyze.Application.News;
using Valyze.Application.Portfolio;
using Valyze.Domain.Application.Auth;
using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Application.Ingestion;
using Valyze.Domain.Application.News;
using Valyze.Domain.Application.Portfolio;

namespace Valyze.Application;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Portfolio
        services.AddScoped<IGetPortfolioUseCase, GetPortfolioUseCase>();
        services.AddScoped<IGetPositionsUseCase, GetPositionsUseCase>();

        // Auth
        services.AddScoped<IDevLoginUseCase, DevLoginUseCase>();

        // Ingestion
        services.AddScoped<IImportTradesUseCase, ImportTradesUseCase>();

        // News
        services.AddScoped<IListNewsSourcesUseCase, ListNewsSourcesUseCase>();
        services.AddScoped<IAddNewsSourceUseCase, AddNewsSourceUseCase>();
        services.AddScoped<IDisableNewsSourceUseCase, DisableNewsSourceUseCase>();
        services.AddScoped<IGetNewsForSymbolUseCase, GetNewsForSymbolUseCase>();
        services.AddScoped<IGetLatestNewsUseCase, GetLatestNewsUseCase>();
        services.AddScoped<IRefreshNewsUseCase, RefreshNewsUseCase>();

        // Decisions — bind config section; default values in DecisionEvaluationOptions are used if key is absent.
        services.Configure<DecisionEvaluationOptions>(opts =>
        {
            var section = configuration.GetSection("Decisions:Evaluation");
            var threshold = section["AchievementThreshold"];
            if (!string.IsNullOrEmpty(threshold) &&
                decimal.TryParse(threshold, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                opts.AchievementThreshold = val;
        });
        services.AddScoped<IRecordDecisionUseCase, RecordDecisionUseCase>();
        services.AddScoped<IListDecisionsUseCase, ListDecisionsUseCase>();
        services.AddScoped<IEvaluateDecisionUseCase, EvaluateDecisionUseCase>();
        services.AddScoped<IGetDecisionTrackRecordUseCase, GetDecisionTrackRecordUseCase>();
        services.AddScoped<ILinkDecisionToTradeUseCase, LinkDecisionToTradeUseCase>();

        return services;
    }
}
