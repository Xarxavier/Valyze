using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.Application.Portfolio;

public interface IGetPositionsUseCase
{
    Task<PositionsViewEntity> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public sealed class PositionsViewEntity
{
    public Guid AccountId { get; set; }
    public IReadOnlyList<PositionEntity> Positions { get; set; } = [];
    public PortfolioSummaryEntity Summary { get; set; } = new();
}
