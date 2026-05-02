using Valyze.Domain.Entities.Ingestion;

namespace Valyze.Domain.Application.Ingestion;

public interface IImportTradesUseCase
{
    Task<ImportResultEntity> ExecuteAsync(
        Guid accountId,
        string brokerKey,
        Stream pdfStream,
        string fileName,
        CancellationToken cancellationToken = default);
}
