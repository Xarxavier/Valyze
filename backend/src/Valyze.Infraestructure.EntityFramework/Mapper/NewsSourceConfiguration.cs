using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class NewsSourceConfiguration : IEntityTypeConfiguration<NewsSource>
{
    public void Configure(EntityTypeBuilder<NewsSource> builder)
    {
        builder.ToTable("news_sources");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(s => s.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(s => s.UrlTemplate).HasColumnName("url_template").HasMaxLength(1024).IsRequired();
        builder.Property(s => s.Scope).HasColumnName("scope").IsRequired();
        builder.Property(s => s.PollingIntervalMinutes).HasColumnName("polling_interval_minutes").IsRequired();
        builder.Property(s => s.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.LastPolledAt).HasColumnName("last_polled_at");
        builder.Property(s => s.LastError).HasColumnName("last_error");

        builder.HasIndex(s => s.Enabled).HasDatabaseName("ix_news_sources_enabled");
        builder.HasIndex(s => s.UrlTemplate).IsUnique().HasDatabaseName("ix_news_sources_url_unique");
    }
}
