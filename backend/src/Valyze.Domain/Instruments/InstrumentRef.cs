namespace Valyze.Domain.Instruments;

/// <summary>
/// Free-form symbolic reference to a tradable instrument.
/// Holds a real ISIN (e.g. US0378331005), a crypto ticker (e.g. BTC), an
/// exchange ticker (e.g. AAPL), or any operator-defined symbol.
/// Validation is intentionally lax: 1-32 chars, alphanumeric plus . - : _.
/// Always normalized to upper case.
/// </summary>
public readonly record struct InstrumentRef
{
    public const int MaxLength = 32;

    public string Value { get; }

    public InstrumentRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Instrument reference is required.", nameof(value));
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > MaxLength)
            throw new ArgumentException(
                $"Instrument reference must be {MaxLength} characters or fewer.",
                nameof(value));
        foreach (var c in normalized)
        {
            var ok = c is >= '0' and <= '9'
                or >= 'A' and <= 'Z'
                or '.' or '-' or ':' or '_';
            if (!ok)
                throw new ArgumentException(
                    $"Instrument reference contains invalid character '{c}'.",
                    nameof(value));
        }
        Value = normalized;
    }

    public override string ToString() => Value;
}
