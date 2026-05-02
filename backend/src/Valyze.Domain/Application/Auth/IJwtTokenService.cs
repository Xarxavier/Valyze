namespace Valyze.Domain.Application.Auth;

public interface IJwtTokenService
{
    string Issue(Guid accountId, string email);
}
