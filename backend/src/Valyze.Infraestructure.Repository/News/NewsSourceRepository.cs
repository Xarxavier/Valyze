using Microsoft.EntityFrameworkCore;
using Valyze.Domain.Entities.News;
using Valyze.Domain.Repository;
using Valyze.Infraestructure.EntityFramework;
using Valyze.Infraestructure.EntityFramework.Mapper;

namespace Valyze.Infraestructure.Repository.News;

public class NewsSourceRepository : INewsSourceRepository
{
    private readonly ValyzeDbContext _context;

    public NewsSourceRepository(ValyzeDbContext context)
    {
        _context = context;
    }

    public async Task<NewsSourceEntity> CreateAsync(
        NewsSourceEntity source,
        CancellationToken cancellationToken = default)
    {
        if (source.Id == Guid.Empty) source.Id = Guid.NewGuid();
        if (source.CreatedAt == default) source.CreatedAt = DateTimeOffset.UtcNow;

        var ef = NewsSourceMapper.ToEf(source);
        _context.NewsSources.Add(ef);
        await _context.SaveChangesAsync(cancellationToken);
        return NewsSourceMapper.ToDomain(ef);
    }

    public async Task<NewsSourceEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var ef = await _context.NewsSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return ef is null ? null : NewsSourceMapper.ToDomain(ef);
    }

    public async Task UpdatePollingStateAsync(
        Guid id,
        DateTimeOffset polledAt,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        var ef = await _context.NewsSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (ef is null) return;
        ef.LastPolledAt = polledAt;
        ef.LastError = lastError;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(
        Guid id,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var ef = await _context.NewsSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (ef is null) return;
        ef.Enabled = enabled;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
