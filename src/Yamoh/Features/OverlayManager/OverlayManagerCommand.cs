using System.Globalization;
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

    public string CommandName => "update-maintainerr-overlays";

    public string CommandDescription =>
        "Main function of the application. Manage overlays based on Maintainerr status.";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var assetBasePath = _yamohConfiguration.AssetBaseFullPath;
        var backupAssetBasePath = _yamohConfiguration.BackupImageFullPath;

        // Restore only option
        if (await RestoreOnly()) return;

        // Get collections from Maintainerr
        var collections = Guard.Against.NullOrEmpty(await maintainerrClient.GetCollections(),
                message: "Maintainerr returned zero active collections. Check configuration.")
            .ToList();

        var removedOverlays = await RestoreOriginalPostersMissingFromMaintainerr(collections);
        var appliedOverlays = 0;
        var skippedOverlays = 0;
        var skippedBecauseOfError = 0;
        var overlaySettings = AddOverlaySettings.FromConfig(_overlayConfiguration, _yamohConfiguration.FontFullPath);

        // if collection filter has any entries then we filter on it
        collections = FilterMaintainerrCollections(collections);

        // For each collection, process overlays and update state
        foreach (var collection in collections)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Manual termination requested..");
                return;
            }
            if (!collection.IsActive)
            {
                logger.LogInformation("Collection {CollectionName} is not an active collection in Maintainerr",
                    collection.Title);
                continue;
            }

            if (collection.DeleteAfterDays <= 0)
            {
                logger.LogInformation("Collection {CollectionName} does not have a 'Delete after days' value set",
                    collection.Title);
                continue;
            }

            var items = await plexMetadataBuilder.BuildFromMaintainerrCollection(collection);

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Manual termination requested..");
                    return;
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

                var overlayText = _overlayConfiguration.GetOverlayText(item.ExpirationDate);
                var overlayTextChanged = !string.Equals(overlayText,state.OverlayText,StringComparison.Ordinal);

                try
                {
                    if (_overlayBehaviorConfiguration.ReapplyOverlays ||
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
                            skippedBecauseOfError++;
                            continue;
                        }

                        var backupAssetPath = new AssetPathInfo(backupAssetBasePath, item).Directory;

                        if (backupAssetPath == null || !backupAssetPath.TryCreate())
                        {
                            logger.LogInformation("Failed to create backup file directory. Path: {Path}",
                                backupAssetPath?.FullName);
                            skippedBecauseOfError++;
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
                            overlayHelper.AddOverlay(item.PlexId, workAsset, overlayText, overlaySettings);

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

                        appliedOverlays++;

                        logger.LogInformation(
                            "Applied overlay and tracked state for PlexId {ItemPlexId} - {FriendlyTitle}",
                            item.PlexId,
                            item.FriendlyTitle);
                    }
                    else
                    {
                        // Already applied, update LastChecked
                        skippedOverlays++;
                        state.LastChecked = DateTime.UtcNow;
                    }

                    overlayStateManager.Upsert(state);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error updating overlay for {PlexId} - {FriendlyTitle}", item.PlexId,
                        item.FriendlyTitle);
                    skippedBecauseOfError++;
                }
            }
        }

        logger.LogInformation(
            "Overlay operations completed with {RemovedOverlays} removed, {AppliedOverlays} applied, {SkippedOverlays} skipped, and {SkippedDueToError} error skips",
            removedOverlays, appliedOverlays, skippedOverlays, skippedBecauseOfError);
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
            logger.LogWarning("Maintainerr Collections Filter entry {Missing} does not exist in Maintainerr collections",
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
        // Get all PlexIds currently in Maintainerr
        var currentPlexIds = collections.SelectMany(c => c.Media ?? []).Select(m => m.PlexId).ToHashSet();

        // Restore overlays for items no longer in Maintainerr but still in Plex
        var pendingRestores = overlayStateManager.GetNeedsRestoresMissingFromList(currentPlexIds);
        var count = 0;

        foreach (var pendingRestore in pendingRestores)
        {
            if (await RestoreOriginalPoster(pendingRestore, true)) count++;
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
