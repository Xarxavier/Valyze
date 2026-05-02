using Valyze.Domain.Entities.Auth;

namespace Valyze.Domain.Application.Auth;

public interface IDevLoginUseCase
{
    Task<DevLoginResponseEntity> ExecuteAsync(CancellationToken cancellationToken = default);
}
