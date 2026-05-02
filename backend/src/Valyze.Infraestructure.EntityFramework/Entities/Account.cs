namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class Account
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string BaseCurrency { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
