namespace Yamoh.Domain.Plex;

public class PlexMetadata
{
    public int? RatingKey { get; set; }
    public string? Title { get; set; }
    public List<PlexLabel>? Label { get; set; }
}