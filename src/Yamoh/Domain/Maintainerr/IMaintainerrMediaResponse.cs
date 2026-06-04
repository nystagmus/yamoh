namespace Yamoh.Domain.Maintainerr;

public interface IMaintainerrMediaResponse
{
    public int Id { get; }
    public int CollectionId { get; }
    public string? MediaServerId { get; }
    public int TmdbId { get; }
    public DateTimeOffset AddDate { get; }
    public string? ImagePath { get; }
    public bool IsManual { get; }
}