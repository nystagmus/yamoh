namespace Yamoh.Domain.Maintainerr;

using System.Text.Json.Serialization;

public class MaintainerrCollectionResponseV3 : IMaintainerrCollectionResponse
{
    public int Id { get; set; }
    public string? MediaServerId { get; set; }
    public string? MediaServerType { get; set; }
    public string? LibraryId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int? DeleteAfterDays { get; set; }
    [JsonPropertyName("type")]
    public string? TypeRaw { get; set; }
    [JsonIgnore]
    public MaintainerrDataType Type => TypeRaw?.ToLowerInvariant() switch
    {
        "movie" => MaintainerrDataType.Movies,
        "show" => MaintainerrDataType.Shows,
        "season" => MaintainerrDataType.Seasons,
        "episode" => MaintainerrDataType.Episodes,
        _ => MaintainerrDataType.Unknown
    };
    [JsonPropertyName("media")]
    public List<MaintainerrMediaResponseV3>? Media { get; set; }

    [JsonIgnore]
    List<IMaintainerrMediaResponse>? IMaintainerrCollectionResponse.Media =>
        Media?.Cast<IMaintainerrMediaResponse>().ToList();
}