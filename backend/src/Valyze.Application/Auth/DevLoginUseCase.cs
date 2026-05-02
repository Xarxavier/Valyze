using Valyze.Domain.Application.Auth;
using Valyze.Domain.Entities.Auth;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Repository;

namespace Valyze.Application.Auth;

public class DevLoginUseCase : IDevLoginUseCase
{
    private readonly IAccountRepository _accountRepository;
    private readonly IJwtTokenService _jwtTokenService;

    public DevLoginUseCase(IAccountRepository accountRepository, IJwtTokenService jwtTokenService)
    {
        _accountRepository = accountRepository;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<DevLoginResponseEntity> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetFirstAsync(cancellationToken)
            ?? throw new BusinessException("msnPersonalAccountNotSeeded");

        var token = _jwtTokenService.Issue(account.Id, account.Email);

        return new DevLoginResponseEntity
        {
            AccessToken = token,
            AccountId = account.Id,
            Email = account.Email,
        };
    }
}
