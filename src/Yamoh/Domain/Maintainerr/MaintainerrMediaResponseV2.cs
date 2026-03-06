namespace Yamoh.Domain.Maintainerr;

using System.Text.Json.Serialization;

public class MaintainerrMediaResponseV2 : IMaintainerrMediaResponse
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    [JsonPropertyName("plexId")]
    public string? MediaServerId => PlexId;
    public string? PlexId { get; set; }
    public int TmdbId { get; set; }
    public DateTime AddDate { get; set; }
    public string? ImagePath { get; set; }
    public bool IsManual { get; set; }
}