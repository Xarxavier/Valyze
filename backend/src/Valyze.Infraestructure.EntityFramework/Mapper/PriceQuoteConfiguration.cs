using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class PriceQuoteConfiguration : IEntityTypeConfiguration<PriceQuote>
{
    public void Configure(EntityTypeBuilder<PriceQuote> builder)
    {
        builder.ToTable("price_quotes");

        builder.HasKey(q => new { q.Symbol, q.Currency });

        builder.Property(q => q.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(q => q.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(q => q.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric(28, 8)")
            .IsRequired();

        builder.Property(q => q.Source)
            .HasColumnName("source")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(q => q.FetchedAt)
            .HasColumnName("fetched_at")
            .IsRequired();

        builder.HasIndex(q => q.FetchedAt)
            .HasDatabaseName("ix_price_quotes_fetched_at");
    }
}
