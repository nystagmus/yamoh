namespace Yamoh.Domain.Maintainerr;

using System.Text.Json.Serialization;

public class MaintainerrCollectionResponseV2 : IMaintainerrCollectionResponse
{
    public int Id { get; set; }
    public int PlexId { get; set; }
    public string? MediaServerId => PlexId.ToString();
    public string? MediaServerType => "plex";
    [JsonPropertyName("libraryId")]
    public int LibraryIdRaw { get; set; }
    [JsonIgnore]
    public string? LibraryId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int? DeleteAfterDays { get; set; }
    [JsonPropertyName("type")]
    public int TypeRaw { get; set; }
    [JsonIgnore]
    public MaintainerrDataType Type => TypeRaw switch
    {
        1 => MaintainerrDataType.Movies,
        2 => MaintainerrDataType.Shows,
        3 => MaintainerrDataType.Seasons,
        4 => MaintainerrDataType.Episodes,
        _ => MaintainerrDataType.Unknown
    };

    [JsonPropertyName("media")]
    public List<MaintainerrMediaResponseV2>? Media { get; set; } = [];

    // Satisfy the interface without affecting deserialization
    [JsonIgnore]
    List<IMaintainerrMediaResponse>? IMaintainerrCollectionResponse.Media =>
        Media?.Cast<IMaintainerrMediaResponse>().ToList();
}