namespace Valyze.Domain.Entities.Ingestion;

public sealed class ImportResultEntity
{
    public string FileName { get; set; } = null!;
    public string BrokerKey { get; set; } = null!;
    public int TradesImported { get; set; }
    public int TradesSkipped { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public string? RawTextSample { get; set; }
}
