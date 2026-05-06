using Valyze.Domain.Application.Decisions;
using Valyze.Domain.Decisions;
using Valyze.Domain.Entities.Decisions;
using Valyze.Domain.Entities.Identity;
using Valyze.Domain.Enum;

namespace Valyze.Host.MinimalApi.Decisions;

public static class DecisionEndpoints
{
    public static RouteGroupBuilder MapDecisionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/decisions")
            .WithTags("Decisions")
            .RequireAuthorization();

        // POST /api/decisions — record a new investment decision
        group.MapPost("/", async (
            IRecordDecisionUseCase useCase,
            AccessorClassEntity accessor,
            RecordDecisionBody body,
            CancellationToken ct) =>
        {
            var result = await useCase.ExecuteAsync(new RecordDecisionCommand(
                AccountId: accessor.AccountId,
                Source: body.Source,
                Action: body.Action,
                Isin: body.Isin,
                Ticker: body.Ticker,
                QuantityAmount: body.QuantityAmount,
                QuantityCurrency: body.QuantityCurrency,
                QuantityUnits: body.QuantityUnits,
                Rationale: body.Rationale,
                EvaluationHorizonDays: body.HorizonDays,
                SourceOtherNote: body.SourceOtherNote), ct);

            return Results.Created(
                $"/api/decisions/{result.Id}",
                new { id = result.Id, warning = result.Warning });
        }).WithName("RecordDecision");

        // GET /api/decisions — list decisions with optional filters
        group.MapGet("/", async (
            IListDecisionsUseCase useCase,
            AccessorClassEntity accessor,
            int? limit,
            DateTimeOffset? since,
            DecisionSource? source,
            DecisionAction? action,
            string? isin,
            CancellationToken ct) =>
        {
            var decisions = await useCase.ExecuteAsync(new ListDecisionsQuery(
                AccountId: accessor.AccountId,
                Limit: limit,
                Since: since,
                Source: source,
                Action: action,
                Isin: isin), ct);

            return Results.Ok(new
            {
                count = decisions.Count,
                decisions = decisions.Select(ToDecisionDto).ToArray(),
            });
        }).WithName("ListDecisions");

        // GET /api/decisions/{id}/evaluate — evaluate a single decision outcome
        group.MapGet("/{id:guid}/evaluate", async (
            IEvaluateDecisionUseCase useCase,
            AccessorClassEntity accessor,
            Guid id,
            CancellationToken ct) =>
        {
            var evaluation = await useCase.ExecuteAsync(id, accessor.AccountId, ct);
            return Results.Ok(ToEvaluationDto(evaluation));
        }).WithName("EvaluateDecision");

        // GET /api/decisions/track-record — aggregated hit-rate stats
        group.MapGet("/track-record", async (
            IGetDecisionTrackRecordUseCase useCase,
            AccessorClassEntity accessor,
            DecisionSource? source,
            CancellationToken ct) =>
        {
            var record = await useCase.ExecuteAsync(accessor.AccountId, source, ct);
            return Results.Ok(new
            {
                bySource = record.BySource.Select(ToTrackRecordRowDto).ToArray(),
            });
        }).WithName("GetDecisionTrackRecord");

        // PATCH /api/decisions/{id}/link-trade — link or unlink a trade
        group.MapMethods("/{id:guid}/link-trade", ["PATCH"], async (
            ILinkDecisionToTradeUseCase useCase,
            AccessorClassEntity accessor,
            Guid id,
            LinkTradeBody body,
            CancellationToken ct) =>
        {
            await useCase.ExecuteAsync(id, accessor.AccountId, body.TradeId, ct);
            return Results.NoContent();
        }).WithName("LinkDecisionToTrade");

        return group;
    }

    // ── Request bodies ─────────────────────────────────────────────────────────

    public sealed class RecordDecisionBody
    {
        public DecisionSource Source { get; set; }
        public DecisionAction Action { get; set; }
        public string? Isin { get; set; }
        public string? Ticker { get; set; }
        public decimal? QuantityAmount { get; set; }
        public string? QuantityCurrency { get; set; }
        public QuantityUnits QuantityUnits { get; set; } = QuantityUnits.Shares;
        public string Rationale { get; set; } = null!;
        public int? HorizonDays { get; set; }
        public string? SourceOtherNote { get; set; }
    }

    public sealed class LinkTradeBody
    {
        public Guid? TradeId { get; set; }
    }

    // ── DTO projections ────────────────────────────────────────────────────────

    private static object ToDecisionDto(InvestmentDecisionEntity d) => new
    {
        id = d.Id,
        accountId = d.AccountId,
        source = d.Source.ToString(),
        action = d.Action.ToString(),
        isin = d.Isin,
        ticker = d.Ticker,
        quantityAmount = d.QuantityAmount,
        quantityCurrency = d.QuantityCurrency?.Code,
        quantityUnits = d.QuantityUnits.ToString(),
        priceAtDecision = d.PriceAtDecision.HasValue
            ? new { amount = d.PriceAtDecision.Value.Amount, currency = d.PriceAtDecision.Value.Currency.Code }
            : null as object,
        rationale = d.Rationale,
        evaluationHorizonDays = d.EvaluationHorizonDays,
        linkedTradeId = d.LinkedTradeId,
        sourceOtherNote = d.SourceOtherNote,
        createdAt = d.CreatedAt,
        updatedAt = d.UpdatedAt,
    };

    private static object ToEvaluationDto(DecisionEvaluation e) => new
    {
        status = e.Status.ToString(),
        returnPercent = e.ReturnPercent,
        daysElapsed = e.DaysElapsed,
        horizon = e.Horizon,
        priceThen = e.PriceThen.HasValue
            ? new { amount = e.PriceThen.Value.Amount, currency = e.PriceThen.Value.Currency.Code }
            : null as object,
        priceNow = e.PriceNow.HasValue
            ? new { amount = e.PriceNow.Value.Amount, currency = e.PriceNow.Value.Currency.Code }
            : null as object,
        message = e.Message,
    };

    private static object ToTrackRecordRowDto(DecisionTrackRecordRow r) => new
    {
        source = r.Source.ToString(),
        total = r.Total,
        achieved = r.Achieved,
        underperforming = r.Underperforming,
        pending = r.Pending,
        notApplicable = r.NotApplicable,
        mixed = r.Mixed,
        avgReturnPercent = r.AvgReturnPercent,
    };
}
