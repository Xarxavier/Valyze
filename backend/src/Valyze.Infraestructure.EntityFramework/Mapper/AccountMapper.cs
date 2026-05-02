using Valyze.Domain.Entities.Identity;
using Valyze.Domain.Money;

namespace Valyze.Infraestructure.EntityFramework.Mapper;

public static class AccountMapper
{
    public static Entities.Account ToEf(AccountEntity domain) => new()
    {
        Id = domain.Id,
        Email = domain.Email,
        BaseCurrency = domain.BaseCurrency.Code,
        CreatedAt = domain.CreatedAt,
    };

    public static AccountEntity ToDomain(Entities.Account ef) => new()
    {
        Id = ef.Id,
        Email = ef.Email,
        BaseCurrency = new Currency(ef.BaseCurrency),
        CreatedAt = ef.CreatedAt,
    };
}
