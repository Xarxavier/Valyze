namespace Valyze.Domain.Money;

public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money Zero(Currency currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b, "add");
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b, "subtract");
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static Money operator *(Money m, decimal factor) =>
        new(m.Amount * factor, m.Currency);

    public static Money operator /(Money m, decimal divisor) =>
        new(m.Amount / divisor, m.Currency);

    private static void EnsureSameCurrency(Money a, Money b, string op)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException(
                $"Cannot {op} {a.Currency} and {b.Currency}. Convert through an FX rate first.");
    }

    public override string ToString() => $"{Amount} {Currency}";
}
