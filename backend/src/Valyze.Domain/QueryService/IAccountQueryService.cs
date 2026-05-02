using Valyze.Domain.Entities.Identity;

namespace Valyze.Domain.QueryService;

public interface IAccountQueryService
{
    Task<AccountEntity?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default);
}
