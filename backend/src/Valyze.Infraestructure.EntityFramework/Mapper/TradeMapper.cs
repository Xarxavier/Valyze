using Valyze.Domain.Entities.Portfolio;
using Valyze.Domain.Enum;
using Valyze.Domain.Instruments;
using Valyze.Domain.Money;
using MoneyValue = Valyze.Domain.Money.Money;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class TradeMapper
{
    public static Entities.Trade ToEf(TradeEntity domain) => new()
    {
        Id = domain.Id,
        AccountId = domain.AccountId,
        Instrument = domain.Instrument.Value,
        Side = (short)domain.Side,
        Quantity = domain.Quantity,
        PriceAmount = domain.Price.Amount,
        PriceCurrency = domain.Price.Currency.Code,
        FeesAmount = domain.Fees.Amount,
        FeesCurrency = domain.Fees.Currency.Code,
        ExecutedAt = domain.ExecutedAt,
        BrokerKey = domain.BrokerKey,
        BrokerReference = domain.BrokerReference,
        Name = domain.Name,
    };

    public static TradeEntity ToDomain(Entities.Trade ef) => new()
    {
        Id = ef.Id,
        AccountId = ef.AccountId,
        Instrument = new InstrumentRef(ef.Instrument),
        Side = (TradeSide)ef.Side,
        Quantity = ef.Quantity,
        Price = new MoneyValue(ef.PriceAmount, new Currency(ef.PriceCurrency)),
        Fees = new MoneyValue(ef.FeesAmount, new Currency(ef.FeesCurrency)),
        ExecutedAt = ef.ExecutedAt,
        BrokerKey = ef.BrokerKey,
        BrokerReference = ef.BrokerReference,
        Name = ef.Name,
    };
}
