using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Money;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class PriceQuoteMapper
{
    public static Entities.PriceQuote ToEf(PriceQuoteEntity domain) => new()
    {
        Symbol = domain.Symbol,
        Currency = domain.Currency.Code,
        Amount = domain.Amount,
        Source = domain.Source,
        FetchedAt = domain.FetchedAt,
    };

    public static PriceQuoteEntity ToDomain(Entities.PriceQuote ef) => new()
    {
        Symbol = ef.Symbol,
        Currency = new Currency(ef.Currency),
        Amount = ef.Amount,
        Source = ef.Source,
        FetchedAt = ef.FetchedAt,
    };
}
