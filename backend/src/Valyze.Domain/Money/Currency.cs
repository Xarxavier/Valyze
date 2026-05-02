namespace Valyze.Domain.Money;

public readonly record struct Currency
{
    public string Code { get; }

    public Currency(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Currency code is required.", nameof(code));
        if (code.Length != 3)
            throw new ArgumentException("Currency code must be 3 characters (ISO 4217).", nameof(code));
        Code = code.ToUpperInvariant();
    }

    public static Currency Eur { get; } = new("EUR");
    public static Currency Usd { get; } = new("USD");
    public static Currency Gbp { get; } = new("GBP");

    public override string ToString() => Code;
}
