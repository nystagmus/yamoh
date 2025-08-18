namespace YAMOH.Commands;

public class OverlayManagerItem
{
    public required int PlexId { get; set; }
    public required MaintainerrPlexDataType DataType { get; set; }
    public required string LibraryName { get; set; }
    public required string LibraryPath { get; set; }
    public required string MediaFilePath { get; set; }
    public bool HasExpiration { get; set; }
    public DateTimeOffset ExpirationDate { get; set; }
}