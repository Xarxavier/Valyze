using Valyze.Domain.Application.Ingestion;
using Valyze.Domain.Entities.Ingestion;
using Valyze.Domain.Exceptions;
using Valyze.Domain.Repository;

namespace Valyze.Application.Ingestion;

public class ImportTradesUseCase : IImportTradesUseCase
{
    private readonly IEnumerable<IBrokerAdapter> _adapters;
    private readonly ITradeRepository _tradeRepository;

    public ImportTradesUseCase(IEnumerable<IBrokerAdapter> adapters, ITradeRepository tradeRepository)
    {
        _adapters = adapters;
        _tradeRepository = tradeRepository;
    }

    public async Task<ImportResultEntity> ExecuteAsync(
        Guid accountId,
        string brokerKey,
        Stream pdfStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            throw new BusinessException("msnAccountIdRequired");
        if (string.IsNullOrWhiteSpace(brokerKey))
            throw new BusinessException("msnBrokerKeyRequired");

        var adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.BrokerKey, brokerKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new BusinessException("msnBrokerNotSupported", $"No adapter registered for broker '{brokerKey}'.");

        var parsed = await adapter.ParseAsync(pdfStream, fileName, cancellationToken);

        foreach (var trade in parsed.Trades)
        {
            trade.AccountId = accountId;
            trade.BrokerKey = adapter.BrokerKey;
        }

        var refs = parsed.Trades
            .Where(t => !string.IsNullOrWhiteSpace(t.BrokerReference))
            .Select(t => t.BrokerReference!)
            .ToList();

        var existing = refs.Count > 0
            ? await _tradeRepository.FindExistingReferencesAsync(accountId, adapter.BrokerKey, refs, cancellationToken)
            : new HashSet<string>(StringComparer.Ordinal);

        var toInsert = parsed.Trades
            .Where(t => string.IsNullOrWhiteSpace(t.BrokerReference) || !existing.Contains(t.BrokerReference!))
            .ToList();
        var skipped = parsed.Trades.Count - toInsert.Count;

        if (toInsert.Count > 0)
            await _tradeRepository.CreateManyAsync(toInsert, cancellationToken);

        var warnings = parsed.Warnings.ToList();
        if (skipped > 0)
            warnings.Add($"Skipped {skipped} duplicate trade(s) already imported (matched on broker reference).");

        return new ImportResultEntity
        {
            FileName = fileName,
            BrokerKey = adapter.BrokerKey,
            TradesImported = toInsert.Count,
            TradesSkipped = skipped,
            Warnings = warnings,
            RawTextSample = parsed.RawTextSample,
        };
    }
}
