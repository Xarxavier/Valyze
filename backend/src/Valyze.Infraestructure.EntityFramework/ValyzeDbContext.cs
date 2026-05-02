using Microsoft.EntityFrameworkCore;
using Valyze.Infraestructure.EntityFramework.Entities;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.EntityFramework;

public sealed class ValyzeDbContext : DbContext
{
    public ValyzeDbContext(DbContextOptions<ValyzeDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<PriceQuote> PriceQuotes => Set<PriceQuote>();

    public DbSet<NewsSource> NewsSources => Set<NewsSource>();
    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();
    public DbSet<NewsArticleInstrument> NewsArticleInstruments => Set<NewsArticleInstrument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new TradeConfiguration());
        modelBuilder.ApplyConfiguration(new PriceQuoteConfiguration());
        modelBuilder.ApplyConfiguration(new NewsSourceConfiguration());
        modelBuilder.ApplyConfiguration(new NewsArticleConfiguration());
        modelBuilder.ApplyConfiguration(new NewsArticleInstrumentConfiguration());
    }
}
