using Microsoft.EntityFrameworkCore;
using Valyze.Application;
using Valyze.Host;
using Valyze.Host.Authorization;
using Valyze.Host.MinimalApi;
using Valyze.Host.Setup;
using Valyze.Infraestructure.Brokers.TradeRepublic;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.MarketData.CoinGecko;
using Valyze.Infraestructure.MarketData.Ecb;
using Valyze.Infraestructure.MarketData.Yahoo;
using Valyze.Infraestructure.News.Rss;
using Valyze.Infraestructure.QueryService;
using Valyze.Infraestructure.Repository;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddValyzeHost(builder.Configuration)
    .AddValyzeApplication(builder.Configuration)
    .AddValyzeRepositories()
    .AddValyzeQueryServices()
    .AddValyzeEntityFramework(builder.Configuration)
    .AddValyzeBrokerTradeRepublic()
    .AddValyzeMarketDataCoinGecko()
    .AddValyzeMarketDataYahoo()
    .AddValyzeMarketDataEcb()
    .AddValyzeNewsRss();

builder.Services.AddHostedService<Valyze.Host.Setup.NewsCollectionService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ValyzeDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<SeedRunner>();
    await seeder.RunAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseCors(Valyze.Host.ServiceExtensions.CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AccessorClassMiddleware>();

app.MapMinimalApi();

app.Run();

public partial class Program;
