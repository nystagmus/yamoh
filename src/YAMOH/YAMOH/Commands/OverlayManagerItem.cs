namespace YAMOH.Commands;

public class OverlayManagerItem
{
    public required int PlexId { get; set; }
    public required MaintainerrPlexDataType DataType { get; set; }
    public required string LibraryName { get; set; }
    public required string MediaFileRelativePath { get; set; }
    public required string MediaFileName { get; set; }
    public required string OriginalPlexPosterUrl { get; set; }
    public bool HasExpiration { get; set; }
    public DateTimeOffset ExpirationDate { get; set; }
}