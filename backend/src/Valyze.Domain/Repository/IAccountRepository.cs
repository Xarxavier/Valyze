using Valyze.Domain.Entities.Identity;

namespace Valyze.Domain.Repository;

public interface IAccountRepository
{
    Task<AccountEntity> CreateAsync(AccountEntity account, CancellationToken cancellationToken = default);
    Task<AccountEntity?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountEntity?> GetFirstAsync(CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);
}
