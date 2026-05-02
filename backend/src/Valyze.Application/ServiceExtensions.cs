using Microsoft.Extensions.DependencyInjection;
using Valyze.Application.Auth;
using Valyze.Application.Ingestion;
using Valyze.Application.News;
using Valyze.Application.Portfolio;
using Valyze.Domain.Application.Auth;
using Valyze.Domain.Application.Ingestion;
using Valyze.Domain.Application.News;
using Valyze.Domain.Application.Portfolio;

namespace Valyze.Application;

public static class ServiceExtensions
{
    public static IServiceCollection AddValyzeApplication(this IServiceCollection services)
    {
        services.AddScoped<IGetPortfolioUseCase, GetPortfolioUseCase>();
        services.AddScoped<IGetPositionsUseCase, GetPositionsUseCase>();
        services.AddScoped<IDevLoginUseCase, DevLoginUseCase>();
        services.AddScoped<IImportTradesUseCase, ImportTradesUseCase>();
        services.AddScoped<IListNewsSourcesUseCase, ListNewsSourcesUseCase>();
        services.AddScoped<IAddNewsSourceUseCase, AddNewsSourceUseCase>();
        services.AddScoped<IDisableNewsSourceUseCase, DisableNewsSourceUseCase>();
        services.AddScoped<IGetNewsForSymbolUseCase, GetNewsForSymbolUseCase>();
        services.AddScoped<IGetLatestNewsUseCase, GetLatestNewsUseCase>();
        services.AddScoped<IRefreshNewsUseCase, RefreshNewsUseCase>();
        return services;
    }
}
