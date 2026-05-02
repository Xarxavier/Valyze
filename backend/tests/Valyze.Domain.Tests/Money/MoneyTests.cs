using Shouldly;
using Valyze.Domain.Money;
using Xunit;

namespace Valyze.Domain.Tests.Money;

public sealed class MoneyTests
{
    [Fact]
    public void Adds_amounts_when_currencies_match()
    {
        var a = new Valyze.Domain.Money.Money(100m, Currency.Eur);
        var b = new Valyze.Domain.Money.Money(50m, Currency.Eur);

        var sum = a + b;

        sum.Amount.ShouldBe(150m);
        sum.Currency.ShouldBe(Currency.Eur);
    }

    [Fact]
    public void Throws_when_adding_different_currencies()
    {
        var euros = new Valyze.Domain.Money.Money(100m, Currency.Eur);
        var dollars = new Valyze.Domain.Money.Money(100m, Currency.Usd);

        Should.Throw<InvalidOperationException>(() => euros + dollars);
    }

    [Fact]
    public void Subtracts_amounts_when_currencies_match()
    {
        var a = new Valyze.Domain.Money.Money(100m, Currency.Usd);
        var b = new Valyze.Domain.Money.Money(30m, Currency.Usd);

        (a - b).Amount.ShouldBe(70m);
    }

    [Fact]
    public void Multiplies_by_scalar()
    {
        var price = new Valyze.Domain.Money.Money(150m, Currency.Usd);
        var total = price * 10m;

        total.Amount.ShouldBe(1500m);
        total.Currency.ShouldBe(Currency.Usd);
    }

    [Fact]
    public void Currency_normalizes_to_upper_case()
    {
        var c = new Currency("eur");
        c.Code.ShouldBe("EUR");
    }

    [Fact]
    public void Currency_rejects_wrong_length()
    {
        Should.Throw<ArgumentException>(() => new Currency("EU"));
        Should.Throw<ArgumentException>(() => new Currency("EURO"));
    }
}
