using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.Identity;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.Repository.Identity;

public class AccountRepository : IAccountRepository
{
    private readonly ValyzeDbContext _context;

    public AccountRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    public async Task<AccountEntity> CreateAsync(AccountEntity account, CancellationToken cancellationToken = default)
    {
        var ef = AccountMapper.ToEf(account);
        _context.Accounts.Add(ef);
        await _context.SaveChangesAsync(cancellationToken);
        return AccountMapper.ToDomain(ef);
    }

    public async Task<AccountEntity?> GetByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var ef = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        return ef is null ? null : AccountMapper.ToDomain(ef);
    }

    public async Task<AccountEntity?> GetFirstAsync(CancellationToken cancellationToken = default)
    {
        var ef = await _context.Accounts.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return ef is null ? null : AccountMapper.ToDomain(ef);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        _context.Accounts.AnyAsync(cancellationToken);
}
