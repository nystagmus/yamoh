namespace Yamoh.Features.OverlayManager;

public class OverlayManagerCommandStats
{
    public int RemovedOverlays { get; set; }
    public int AppliedOverlays { get; set; }
    public int SkippedOverlays { get; set; }
    public int SkippedBecauseOfError { get; set; }
    public int SortedItems { get; set; }
    public List<string> SortedCollections { get; set; } = [];

    public override string ToString()
    {
        var sortedCollections = string.Join(", ", SortedCollections.Select(x => $"'{x}'"));

        var statsString =
            $"{RemovedOverlays} removed, {AppliedOverlays} applied, {SkippedOverlays} skipped, {SkippedBecauseOfError} error skips, and sorted {sortedCollections} collections";
        return statsString;
    }
}
