namespace Yamoh.Infrastructure.Configuration;

public class OverlayBehaviorConfiguration
{
    public static string Position => "OverlayBehavior";

    public bool UseAssetMode { get; init; } = true;
    public bool ReapplyOverlays { get; init; }
    public bool OverlayShowSeasons { get; init; }
    public bool OverlaySeasonEpisodes { get; init; }
    public bool ManageKometaOverlayLabel { get; init; }
    public bool SortPlexCollections { get; set; }
    public SortPlexCollectionDirection SortDirection { get; set; } = SortPlexCollectionDirection.Asc;
    public bool RestoreOnly { get; init; }

    public List<string> MaintainerrCollectionsFilter { get; set; } = [];
}