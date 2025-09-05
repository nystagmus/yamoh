using Yamoh.Domain.Maintainerr;

namespace Yamoh.Features.OverlayManager;

public class PlexMetadataBuilderItem
{
    public required int PlexId { get; init; }
    public required string FriendlyTitle { get; set; }
    public required MaintainerrPlexDataType DataType { get; init; }
    public required string LibraryName { get; init; }
    public required string MediaFileRelativePath { get; init; }
    public required string MediaFileName { get; init; }
    public required string OriginalPlexPosterUrl { get; init; }
    public bool HasExpiration { get; set; }
    public DateTimeOffset ExpirationDate { get; set; }
    public bool IsChild { get; set; }
    public int? ParentPlexId { get; set; }
    public bool KometaLabelExists { get; set; }
    public required int LibraryId { get; set; }
}
