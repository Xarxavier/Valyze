namespace Valyze.Infraestructure.EntityFramework.Entities;

public sealed class NewsSource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string UrlTemplate { get; set; } = null!;
    public short Scope { get; set; }
    public int PollingIntervalMinutes { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastPolledAt { get; set; }
    public string? LastError { get; set; }
}
