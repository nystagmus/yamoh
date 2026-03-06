namespace Yamoh.Domain.Maintainerr;

public class MaintainerrMediaResponseV2 : IMaintainerrMediaResponse
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string? MediaServerId => PlexId.ToString();
    public int PlexId { get; set; }
    public int TmdbId { get; set; }
    public DateTime AddDate { get; set; }
    public string? ImagePath { get; set; }
    public bool IsManual { get; set; }
}