namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class NewsArticleInstrument
{
    public Guid ArticleId { get; set; }
    public string Instrument { get; set; } = null!;
    public double Confidence { get; set; }
}
