namespace Valyze.Domain.Entities.News;

/// <summary>
/// M:N link between an article and the instruments mentioned in it.
/// Confidence is reserved for future sentiment/LLM tagging — for v1 every
/// row uses <c>1.0</c> from the case-insensitive contains match.
/// </summary>
public sealed class NewsArticleInstrumentEntity
{
    public Guid ArticleId { get; set; }

    /// <summary>Same shape as TradeEntity.Instrument — ISIN or ticker.</summary>
    public string Instrument { get; set; } = null!;

    public double Confidence { get; set; } = 1.0;
}
