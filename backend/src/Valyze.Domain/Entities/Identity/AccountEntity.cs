using Valyze.Domain.Money;

namespace Valyze.Domain.Entities.Identity;

public sealed class AccountEntity
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public Currency BaseCurrency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
