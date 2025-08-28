namespace Yamoh.Domain.Maintainerr;

public class MaintainerrCollection
{
    public int Id { get; set; }
    public int PlexId { get; set; }
    public int LibraryId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int ArrAction { get; set; }
    public bool VisibleOnRecommended { get; set; }
    public bool VisibleOnHome { get; set; }
    public int DeleteAfterDays { get; set; }
    public bool ManualCollection { get; set; }
    public string? ManualCollectionName { get; set; }
    public bool ListExclusions { get; set; }
    public bool ForceOverseerr { get; set; }
    public int Type { get; set; }
    public int KeepLogsForMonths { get; set; }
    public string? AddDate { get; set; }
    public int HandledMediaAmount { get; set; }
    public int LastDurationInSeconds { get; set; }
    public int? TautulliWatchedPercentOverride { get; set; }
    public int? RadarrSettingsId { get; set; }
    public int? SonarrSettingsId { get; set; }
    public List<MaintainerrMedia>? Media { get; set; }
}