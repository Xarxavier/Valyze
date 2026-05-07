using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Valyze.Infraestructure.EntityFramework.Entities;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

internal sealed class InvestmentDecisionConfiguration : IEntityTypeConfiguration<InvestmentDecision>
{
    public void Configure(EntityTypeBuilder<InvestmentDecision> builder)
    {
        builder.ToTable("investment_decisions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        // ─── Account FK (CASCADE — account deletion removes all its decisions) ──

        builder.Property(d => d.AccountId)
            .HasColumnName("account_id")
            .IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(d => d.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // ─── Discriminator columns ────────────────────────────────────────────

        builder.Property(d => d.Source)
            .HasColumnName("source")
            .IsRequired();

        builder.Property(d => d.Action)
            .HasColumnName("action")
            .IsRequired();

        // ─── Instrument ───────────────────────────────────────────────────────

        builder.Property(d => d.Isin)
            .HasColumnName("isin")
            .HasMaxLength(12); // ISIN is always 12 chars; nullable for REBALANCE

        builder.Property(d => d.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(20);

        // ─── Quantity ─────────────────────────────────────────────────────────

        builder.Property(d => d.QuantityAmount)
            .HasColumnName("quantity_amount")
            .HasColumnType("numeric(28, 8)");

        builder.Property(d => d.QuantityCurrency)
            .HasColumnName("quantity_currency")
            .HasMaxLength(3); // ISO 4217

        builder.Property(d => d.QuantityUnits)
            .HasColumnName("quantity_units")
            .IsRequired();

        // ─── Price snapshot (Money pair — both NULL or both set) ──────────────

        builder.Property(d => d.PriceAtDecisionAmount)
            .HasColumnName("price_at_decision_amount")
            .HasColumnType("numeric(28, 8)");

        builder.Property(d => d.PriceAtDecisionCurrency)
            .HasColumnName("price_at_decision_currency")
            .HasMaxLength(3); // ISO 4217

        // ─── Decision metadata ────────────────────────────────────────────────

        builder.Property(d => d.Rationale)
            .HasColumnName("rationale")
            .IsRequired();

        builder.Property(d => d.EvaluationHorizonDays)
            .HasColumnName("evaluation_horizon_days")
            .IsRequired();

        builder.Property(d => d.AiChatSessionId)
            .HasColumnName("ai_chat_session_id"); // nullable UUID; populated by SDD #3

        builder.Property(d => d.SourceOtherNote)
            .HasColumnName("source_other_note");

        // ─── Linked trade FK (SET NULL — decisions outlive trades) ────────────

        builder.Property(d => d.LinkedTradeId)
            .HasColumnName("linked_trade_id")
            .IsRequired(false);

        builder.HasOne<Trade>()
            .WithMany()
            .HasForeignKey(d => d.LinkedTradeId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ─── Timestamps ───────────────────────────────────────────────────────

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ─── Indexes (4 per design) ───────────────────────────────────────────

        // List decisions ordered by most recent
        builder.HasIndex(d => new { d.AccountId, d.CreatedAt })
            .HasDatabaseName("ix_investment_decisions_account_created_at");

        // Track-record aggregation query path
        builder.HasIndex(d => new { d.AccountId, d.Source, d.Action, d.CreatedAt })
            .HasDatabaseName("ix_investment_decisions_account_source_action_created_at");

        // Linkage flow — find decisions by ISIN for an account
        builder.HasIndex(d => new { d.AccountId, d.Isin })
            .HasFilter(@"""isin"" IS NOT NULL")
            .HasDatabaseName("ix_investment_decisions_account_isin");

        // Reverse lookup from trade to decision
        builder.HasIndex(d => d.LinkedTradeId)
            .HasFilter(@"""linked_trade_id"" IS NOT NULL")
            .HasDatabaseName("ix_investment_decisions_linked_trade_id");
    }
}
