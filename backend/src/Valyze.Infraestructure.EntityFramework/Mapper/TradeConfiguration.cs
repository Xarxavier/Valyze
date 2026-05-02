using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("trades");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.HasIndex(t => t.AccountId);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.Instrument)
            .HasColumnName("instrument")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.Side)
            .HasColumnName("side")
            .IsRequired();

        builder.Property(t => t.Quantity)
            .HasColumnName("quantity")
            .HasColumnType("numeric(28, 8)")
            .IsRequired();

        builder.Property(t => t.PriceAmount)
            .HasColumnName("price_amount")
            .HasColumnType("numeric(28, 8)")
            .IsRequired();

        builder.Property(t => t.PriceCurrency)
            .HasColumnName("price_currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(t => t.FeesAmount)
            .HasColumnName("fees_amount")
            .HasColumnType("numeric(28, 8)")
            .IsRequired();

        builder.Property(t => t.FeesCurrency)
            .HasColumnName("fees_currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(t => t.ExecutedAt)
            .HasColumnName("executed_at")
            .IsRequired();

        builder.Property(t => t.BrokerKey)
            .HasColumnName("broker_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.BrokerReference)
            .HasColumnName("broker_reference")
            .HasMaxLength(100);

        builder.Property(t => t.Name)
            .HasColumnName("instrument_name")
            .HasMaxLength(120);

        // Partial unique index — guarantees no duplicate broker_reference per
        // (account, broker), but allows multiple NULLs for manual entries.
        builder.HasIndex(t => new { t.AccountId, t.BrokerKey, t.BrokerReference })
            .IsUnique()
            .HasFilter(@"""broker_reference"" IS NOT NULL")
            .HasDatabaseName("ix_trades_broker_reference_unique");
    }
}
