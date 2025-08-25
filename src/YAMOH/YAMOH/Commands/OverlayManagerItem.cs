using YAMOH.Models;
using YAMOH.Models.Maintainerr;

namespace YAMOH.Commands;

public class OverlayManagerItem
{
    public required int PlexId { get; init; }
    public required MaintainerrPlexDataType DataType { get; init; }
    public required string LibraryName { get; init; }
    public required string MediaFileRelativePath { get; init; }
    public required string MediaFileName { get; init; }
    public required string OriginalPlexPosterUrl { get; init; }
    public bool HasExpiration { get; set; }
    public DateTimeOffset ExpirationDate { get; set; }
    public bool IsChild { get; set; }
    public string? ParentPlexId { get; set; }
    public bool KometaOverlayApplied { get; set; }
    public required long LibraryId { get; set; }
}
