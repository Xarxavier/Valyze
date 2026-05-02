using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class NewsArticleConfiguration : IEntityTypeConfiguration<NewsArticle>
{
    public void Configure(EntityTypeBuilder<NewsArticle> builder)
    {
        builder.ToTable("news_articles");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(a => a.ExternalId).HasColumnName("external_id").HasMaxLength(256);
        builder.Property(a => a.Url).HasColumnName("url").HasMaxLength(1024).IsRequired();
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(512).IsRequired();
        builder.Property(a => a.Summary).HasColumnName("summary");
        builder.Property(a => a.PublishedAt).HasColumnName("published_at").IsRequired();
        builder.Property(a => a.FetchedAt).HasColumnName("fetched_at").IsRequired();
        builder.Property(a => a.Language).HasColumnName("language").HasMaxLength(16);

        // Url is the dedup key — same article surfaced by two feeds collapses to one row.
        builder.HasIndex(a => a.Url).IsUnique().HasDatabaseName("ix_news_articles_url_unique");
        builder.HasIndex(a => a.PublishedAt).HasDatabaseName("ix_news_articles_published_at");
        builder.HasIndex(a => a.SourceId).HasDatabaseName("ix_news_articles_source_id");

        builder.HasOne<NewsSource>()
            .WithMany()
            .HasForeignKey(a => a.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
