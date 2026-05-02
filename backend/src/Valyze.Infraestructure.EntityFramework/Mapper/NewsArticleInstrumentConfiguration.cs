using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class NewsArticleInstrumentConfiguration : IEntityTypeConfiguration<NewsArticleInstrument>
{
    public void Configure(EntityTypeBuilder<NewsArticleInstrument> builder)
    {
        builder.ToTable("news_article_instruments");
        builder.HasKey(t => new { t.ArticleId, t.Instrument });

        builder.Property(t => t.ArticleId).HasColumnName("article_id").IsRequired();
        builder.Property(t => t.Instrument).HasColumnName("instrument").HasMaxLength(32).IsRequired();
        builder.Property(t => t.Confidence).HasColumnName("confidence").IsRequired();

        builder.HasIndex(t => t.Instrument).HasDatabaseName("ix_news_article_instruments_instrument");

        builder.HasOne<NewsArticle>()
            .WithMany()
            .HasForeignKey(t => t.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
