namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class PriceQuote
{
    public string Symbol { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Source { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
