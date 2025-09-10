using Ardalis.GuardClauses;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yamoh.Domain.Maintainerr;
using Yamoh.Domain.State;
using Yamoh.Infrastructure;
using Yamoh.Infrastructure.Configuration;
using Yamoh.Infrastructure.Extensions;
using Yamoh.Infrastructure.External;
using Yamoh.Infrastructure.FileProcessing;
using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Features.OverlayManager;

public class OverlayManagerCommand(
    ILogger<OverlayManagerCommand> logger,
    IOptions<YamohConfiguration> yamohConfigurationOptions,
    IOptions<OverlayConfiguration> overlayConfigurationOptions,
    IOptions<OverlayBehaviorConfiguration> overlayBehaviorConfigurationOptions,
    MaintainerrClient maintainerrClient,
    PlexClient plexClient,
    PlexMetadataBuilder plexMetadataBuilder,
    AssetManager assetManager,
    OverlayHelper overlayHelper,
    OverlayStateManager overlayStateManager) : IYamohCommand
{
    private readonly YamohConfiguration _yamohConfiguration = yamohConfigurationOptions.Value;
    private readonly OverlayConfiguration _overlayConfiguration = overlayConfigurationOptions.Value;

    private readonly OverlayBehaviorConfiguration _overlayBehaviorConfiguration =
        overlayBehaviorConfigurationOptions.Value;

    private readonly AddOverlaySettings _overlaySettings =
        AddOverlaySettings.FromConfig(overlayConfigurationOptions.Value, yamohConfigurationOptions.Value.FontFullPath);

    public string CommandName => "update-maintainerr-overlays";

    public string CommandDescription =>
        "Main function of the application. Manage overlays based on Maintainerr status.";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Restore only option
        if (await RestoreOnly()) return;

        var collections = await maintainerrClient.GetCollections();

        if (collections.Count == 0)
        {
            logger.LogInformation(
                "Zero collections fetched from Maintainerr. If this is unexpected, please check your configuration");
        }

        var stats = new OverlayManagerCommandStats
        {
            RemovedOverlays = await RestoreOriginalPostersMissingFromMaintainerr(collections)
        };

        // if collection filter has any entries then we filter on it
        collections = FilterMaintainerrCollections(collections);

        // For each collection, process overlays and update state
        foreach (var collection in collections)
        {
            logger.LogDebug("Processing items for {CollectionTitle}", collection.Title);

            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Manual termination requested..");
                return;
            }

            if (!CanProcessCollection(collection))
            {
                continue;
            }

            var items = (await plexMetadataBuilder.BuildFromMaintainerrCollection(collection)).ToList();

            if (items.Count == 0)
            {
                logger.LogInformation("Was unable to fetch Plex metadata for {CollectionTitle}. Skipping collection..",
                    collection.Title);
                continue;
            }

            if (!await ProcessItem(items, collection, stats, cancellationToken))
            {
                continue;
            }

            await ReorderCollection(items, collection, stats, cancellationToken);
        }

        logger.LogInformation("Overlay operations completed. Stats: {Stats}", stats);
    }

    private async Task<bool> ReorderCollection(List<PlexMetadataBuilderItem> items, MaintainerrCollection collection,
        OverlayManagerCommandStats stats, CancellationToken cancellationToken)
    {
        if (!this._overlayBehaviorConfiguration.SortPlexCollections || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // Find collection
        var plexCollectionsResponse = await plexClient.GetPlexCollectionsAsync(collection.LibraryId);

        if (plexCollectionsResponse?.MediaContainer?.Metadata == null)
        {
            logger.LogError("Could not fetch collections for LibraryId: {LibraryId}", collection.LibraryId);
            return false;
        }

        var plexCollectionMatch = plexCollectionsResponse.MediaContainer.Metadata
            .FirstOrDefault(x => x.Title == collection.Title)?.RatingKey;

        if (plexCollectionMatch == null)
        {
            logger.LogError(
                "Failed to find matching collection for LibraryId: {LibraryId}, Maintainerr Collection:{MaintainerrCollection}",
                collection.LibraryId, collection.Title);
            return false;
        }

        var plexCollectionRatingKey = plexCollectionMatch.Value;

        // Ensure Plex collection is in custom sort mode
        if (!await plexClient.PutPlexCollectionManualSortOrder(plexCollectionRatingKey))
        {
            logger.LogError("Failed to set Plex Collection '{CollectionTitle}' to custom sort mode", collection.Title);
            return false;
        }

        // only work with the items in the collection
        var collectionIds = collection.Media?.Select(x => x.PlexId).ToList();
        if (collectionIds == null || collectionIds.Count == 0) return false;
        var collectionItems = items.Where(x => collectionIds.Contains(x.PlexId)).ToList();

        var sortedItems = collectionItems.OrderBy(x => x.ExpirationDate).ToList();

        if (this._overlayBehaviorConfiguration.SortDirection == SortPlexCollectionDirection.Desc)
        {
            sortedItems = collectionItems.OrderByDescending(x => x.ExpirationDate).ToList();
        }

        // sort the items in plex
        for (var i = 1; i < sortedItems.Count; i++)
        {
            var item = sortedItems[i];
            var predecessor = sortedItems[i - 1];

            if (await plexClient.PutPlexCollectionItemAfter(plexCollectionRatingKey, item.PlexId, predecessor.PlexId))
            {
                stats.SortedItems++;

                logger.LogDebug("Moved {ItemTitle}({ItemPlexId}) after {PredecessorTitle}({PredecessorPlexId})",
                    item.FriendlyTitle, item.PlexId, predecessor.FriendlyTitle, predecessor.PlexId);
                continue;
            }

            logger.LogError("Error encountered sorting Plex collection {CollectionTitle}", collection.Title);
            return false;
        }

        stats.SortedCollections.Add(
            !string.IsNullOrWhiteSpace(collection.Title)
                ? collection.Title
                : collection.PlexId != 0
                    ? $"Collection {collection.PlexId}"
                    : "Unknown Collection");
        return true;
    }

    private bool CanProcessCollection(MaintainerrCollection collection)
    {
        if (!collection.IsActive)
        {
            logger.LogInformation(
                "Collection {CollectionTitle} is not an active collection in Maintainerr. Skipping collection..",
                collection.Title);
            return false;
        }

        if (collection.DeleteAfterDays <= 0)
        {
            logger.LogInformation(
                "Collection {CollectionTitle} does not have a 'Delete after days' value set. Skipping collection..",
                collection.Title);
            return false;
        }

        if (collection.Media == null || collection.Media.Count == 0)
        {
            logger.LogInformation("Collection {CollectionTitle} has zero media items. Skipping collection..",
                collection.Title);
            return false;
        }

        return true;
    }

    private async Task<bool> ProcessItem(
        List<PlexMetadataBuilderItem> items,
        MaintainerrCollection collection,
        OverlayManagerCommandStats stats,
        CancellationToken cancellationToken)
    {
        var assetBasePath = _yamohConfiguration.AssetBaseFullPath;
        var backupAssetBasePath = _yamohConfiguration.BackupImageFullPath;

        foreach (var item in items)
        {
            logger.LogDebug("Processing item {PlexId}:{FriendlyTitle} from collection {CollectionTitle}",
                item.PlexId, item.FriendlyTitle, collection.Title);

            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Manual termination requested..");
                return false;
            }

            var state = overlayStateManager.GetByPlexId(item.PlexId);

            state ??= new OverlayStateItem
            {
                PlexId = item.PlexId
            };

            state.FriendlyTitle = item.FriendlyTitle;
            state.LibrarySectionId = item.LibraryId;
            state.MaintainerrPlexType = item.DataType;
            state.IsChild = item.IsChild;
            state.ParentPlexId = item.ParentPlexId;

            var overlayText = this._overlayConfiguration.GetOverlayText(item.ExpirationDate);
            var overlayTextChanged = !string.Equals(overlayText, state.OverlayText, StringComparison.Ordinal);

            try
            {
                if (this._overlayBehaviorConfiguration.ReapplyOverlays ||
                    overlayTextChanged ||
                    state is not { OverlayApplied: true })
                {
                    state.MaintainerrCollectionId = collection.Id;
                    state.LastChecked = DateTimeOffset.UtcNow;
                    state.LastKnownExpirationDate = item.ExpirationDate;

                    var asset = new AssetPathInfo(assetBasePath, item);

                    if (asset.Directory == null || !asset.Directory.TryCreate())
                    {
                        logger.LogInformation("Failed to create media file directory. Path: {Path}",
                            asset.FilePath);
                        stats.SkippedBecauseOfError++;
                        continue;
                    }

                    var backupAssetPath = new AssetPathInfo(backupAssetBasePath, item).Directory;

                    if (backupAssetPath == null || !backupAssetPath.TryCreate())
                    {
                        logger.LogInformation("Failed to create backup file directory. Path: {Path}",
                            backupAssetPath?.FullName);
                        stats.SkippedBecauseOfError++;
                        continue;
                    }

                    var workAsset = Guard.Against.Null(await assetManager.GetAndBackupOriginalPoster(
                            backupAssetPath, asset,
                            item.MediaFileName, item.OriginalPlexPosterUrl),
                        message:
                        $"Could not find or fetch original poster for {item.PlexId} - {item.FriendlyTitle}");

                    asset.UpdateExtension(workAsset.File);

                    state.PosterPath = asset.FileName;
                    state.OriginalPosterPath = workAsset.File.FullName;
                    state.KometaLabelExists = item.KometaLabelExists;

                    // Apply overlay

                    var result =
                        overlayHelper.AddOverlay(item.PlexId, workAsset, overlayText, _overlaySettings);

                    result.File.CopyTo(asset.FileName, overwrite: true);
                    result.File.Delete();
                    state.OverlayApplied = true;
                    state.OverlayText = overlayText;
                    state.PosterHash = null;

                    if (item.KometaLabelExists)
                    {
                        if (!await plexClient.RemoveKometaLabelFromItem(item.LibraryId, item.PlexId,
                                item.DataType))
                        {
                            logger.LogInformation(
                                "Failed to remove Kometa Overlay label from PlexId: {PlexId} - {FriendlyTitle}",
                                item.PlexId,
                                item.FriendlyTitle);
                        }

                        state.KometaLabelExists = false;
                    }

                    stats.AppliedOverlays++;

                    logger.LogInformation(
                        "Applied overlay and tracked state for PlexId {ItemPlexId} - {FriendlyTitle}",
                        item.PlexId,
                        item.FriendlyTitle);
                }
                else
                {
                    // Already applied, update LastChecked
                    stats.SkippedOverlays++;
                    state.LastChecked = DateTime.UtcNow;
                }

                overlayStateManager.Upsert(state);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating overlay for {PlexId} - {FriendlyTitle}", item.PlexId,
                    item.FriendlyTitle);
                stats.SkippedBecauseOfError++;
            }
        }

        return true;
    }

    private List<MaintainerrCollection> FilterMaintainerrCollections(List<MaintainerrCollection> collections)
    {
        var filter = this._overlayBehaviorConfiguration.MaintainerrCollectionsFilter;

        if (filter.Count == 0)
        {
            logger.LogInformation(
                "No Maintainerr Collections filter found, processing all active collections with 'DeleteAfterDays' value set");
            return collections.Where(x => x is { IsActive: true, DeleteAfterDays: > 0 }).ToList();
        }

        var collectionTitles = collections
            .Select(x => x.Title?.Trim().ToLowerInvariant())
            .Where(x => x != null)
            .ToHashSet();

        var missingFilters = filter
            .Select(f => new
            {
                Lower = f.Trim().ToLowerInvariant(),
                Original = f
            })
            .Where(f => !collectionTitles.Contains(f.Lower))
            .ToList();

        foreach (var missing in missingFilters)
        {
            logger.LogWarning(
                "Maintainerr Collections Filter entry {Missing} does not exist in Maintainerr collections",
                missing.Original);
        }

        collections = collections
            .Where(x => x.Title != null && filter
                .Any(f => x.Title.Trim().Equals(f.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return collections;
    }

    private async Task<bool> RestoreOnly()
    {
        if (!this._overlayBehaviorConfiguration.RestoreOnly)
        {
            return false;
        }

        logger.LogWarning(
            "[yellow bold]Restore Only[/] was set in configuration. Will restore all posters back to original");
        var restoreItems = overlayStateManager.GetAppliedOverlays().ToList();

        var count = 0;

        foreach (var item in restoreItems)
        {
            if (await RestoreOriginalPoster(item, false))
            {
                count++;
            }
        }

        logger.LogInformation("Restored {Count} images back to their originals", count);
        return true;
    }

    private async Task<int> RestoreOriginalPostersMissingFromMaintainerr(List<MaintainerrCollection> collections)
    {
        var count = 0;

        try
        {
            // Get all PlexIds currently in Maintainerr
            var currentPlexIds = collections.SelectMany(c => c.Media ?? []).Select(m => m.PlexId).ToHashSet();

            // Restore overlays for items no longer in Maintainerr but still in Plex
            var pendingRestores = overlayStateManager.GetNeedsRestoresMissingFromList(currentPlexIds);

            foreach (var pendingRestore in pendingRestores)
            {
                if (await RestoreOriginalPoster(pendingRestore, true)) count++;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error restoring original posters missing from Maintainerr");
        }

        return count;
    }

    private async Task<bool> RestoreOriginalPoster(OverlayStateItem item, bool deleteFromState)
    {
        // Restore poster
        if (AssetManager.TryRestorePoster(item.OriginalPosterPath, item.PosterPath))
        {
            // Remove label
            await plexClient.RemoveKometaLabelFromItem(item.LibrarySectionId, item.PlexId, item.MaintainerrPlexType);

            // Delete if necessary
            if (deleteFromState)
            {
                overlayStateManager.Remove(item.PlexId);
            }
            else
            {
                // Update state
                item.OverlayApplied = false;
                overlayStateManager.Upsert(item);
            }

            logger.LogInformation("Restored original poster for PlexId {StateItemPlexId} '{StateItemFriendlyName}'",
                item.PlexId, item.FriendlyTitle);
            return true;
        }

        logger.LogInformation(
            "Could not restore original poster for PlexId {StateItemPlexId} '{StateItemFriendlyTitle}' - missing?",
            item.PlexId, item.FriendlyTitle);

        return false;
    }
}