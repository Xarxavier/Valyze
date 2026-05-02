namespace Valyze.Domain.Entities.Auth;

public sealed class DevLoginResponseEntity
{
    public string AccessToken { get; set; } = null!;
    public Guid AccountId { get; set; }
    public string Email { get; set; } = null!;
}
