using Valyze.Domain.Entities.Portfolio;

namespace Valyze.Domain.Application.Ingestion;

public interface IBrokerAdapter
{
    string BrokerKey { get; }

    Task<BrokerParseResult> ParseAsync(Stream input, string fileName, CancellationToken cancellationToken = default);
}

public sealed class BrokerParseResult
{
    public IReadOnlyList<TradeEntity> Trades { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? RawTextSample { get; init; }
}
