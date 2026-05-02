namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class Trade
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Instrument { get; set; } = null!;
    public short Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = null!;
    public decimal FeesAmount { get; set; }
    public string FeesCurrency { get; set; } = null!;
    public DateTimeOffset ExecutedAt { get; set; }
    public string BrokerKey { get; set; } = null!;
    public string? BrokerReference { get; set; }
    public string? Name { get; set; }
}
