namespace Valyze.Domain.Enum;

/// <summary>
/// How a news source should be polled.
/// PerSymbol — the URL template contains <c>{symbol}</c>/<c>{name}</c> and the
/// collector expands it once per held instrument.
/// Global — the URL is fetched verbatim; tagging happens against article text.
/// </summary>
public enum NewsSourceScope
{
    PerSymbol = 0,
    Global = 1,
}
