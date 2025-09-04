using System.Globalization;
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

        // Restore only
        if (_overlayBehaviorConfiguration.RestoreOnly)
        {
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
            return;
        }

        // Get collections from Maintainerr
        var collections = (await maintainerrClient.GetCollections())
            .Where(x => x is { IsActive: true, DeleteAfterDays: > 0 }).ToList();

        if (collections.Count == 0)
        {
            logger.LogInformation("Maintainerr returned zero active collections. Check configuration");
            return;
        }

        var removedOverlays = await RestoreOriginalPostersMissingFromMaintainerr(collections);
        var appliedOverlays = 0;
        var skippedOverlays = 0;
        var skippedBecauseOfError = 0;
        var overlaySettings = AddOverlaySettings.FromConfig(_overlayConfiguration, _yamohConfiguration.FontFullPath);

        // For each collection, process overlays and update state
        foreach (var collection in collections.Where(collection => collection.IsActive))
        {
            var items = await plexMetadataBuilder.BuildFromMaintainerrCollection(collection);

            foreach (var item in items)
            {
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

                var shouldReapply = state.LastKnownExpirationDate.Date != item.ExpirationDate.Date;

                if (_overlayBehaviorConfiguration.ReapplyOverlays || shouldReapply
                                                                  || state is not { OverlayApplied: true })
                {
                    state.MaintainerrCollectionId = collection.Id;
                    state.LastChecked = DateTimeOffset.UtcNow;
                    state.LastKnownExpirationDate = item.ExpirationDate;

                    // Save original poster (Asset Mode)
                    var mediaFileFullPath = Path.GetFullPath(Path.Combine(assetBasePath, item.MediaFileRelativePath));
                    var mediaFileFullName = Path.GetFullPath(Path.Combine(mediaFileFullPath, item.MediaFileName));
                    var mediaFileDirectory = new DirectoryInfo(mediaFileFullPath);

                    if (!mediaFileDirectory.TryCreate())
                    {
                        logger.LogInformation("Failed to create media file directory. Path: {Path}", mediaFileFullPath);
                        skippedBecauseOfError++;
                        continue;
                    }

                    var backupFileFullPath =
                        Path.GetFullPath(Path.Combine(backupAssetBasePath, item.MediaFileRelativePath));
                    var backupFileFullName = Path.GetFullPath(Path.Combine(backupFileFullPath, item.MediaFileName));
                    var backupFileDirectory = new DirectoryInfo(backupFileFullPath);

                    if (!backupFileDirectory.TryCreate())
                    {
                        logger.LogInformation("Failed to create backup file directory. Path: {Path}",
                            backupFileFullPath);
                        skippedBecauseOfError++;
                        continue;
                    }

                    // Will get poster.original.jpg first, else poster.jpg if exists, or null
                    var originalPoster =
                        AssetManager.GetOriginalPoster(backupFileDirectory, mediaFileDirectory, item.MediaFileName);
                    var originalPosterBackupFullName = backupFileFullName + AssetManager.BackupFileNameSuffix;

                    if (originalPoster is { Exists: true })
                    {
                        mediaFileFullName += originalPoster.Extension;
                        originalPosterBackupFullName += originalPoster.Extension;

                        if (!File.Exists(originalPosterBackupFullName))
                            File.Copy(originalPoster.FullName, originalPosterBackupFullName, overwrite: true);
                    }
                    else
                    {
                        // download original poster from plex instead
                        var plexPoster = await plexClient.DownloadPlexImageAsync(item.OriginalPlexPosterUrl);

                        if (plexPoster is { Exists: true } && plexPoster.IsImageByExtension())
                        {
                            mediaFileFullName += plexPoster.Extension;
                            originalPosterBackupFullName += plexPoster.Extension;
                            File.Copy(plexPoster.FullName, mediaFileFullName, overwrite: true);
                            File.Copy(plexPoster.FullName, originalPosterBackupFullName, overwrite: true);
                            File.Delete(plexPoster.FullName);
                        }
                        else
                        {
                            skippedBecauseOfError++;

                            logger.LogInformation(
                                "Could not find or fetch original poster for {PlexId} - {FriendlyTitle}", item.PlexId,
                                item.FriendlyTitle);
                            continue;
                        }
                    }

                    state.PosterPath = mediaFileFullName;
                    state.OriginalPosterPath = originalPosterBackupFullName;
                    state.KometaLabelExists = item.KometaLabelExists;

                    // Apply overlay
                    var overlayText = GetOverlayText(item);
                    var result = overlayHelper.AddOverlay(item.PlexId, mediaFileFullName, overlayText, overlaySettings);

                    if (result is { Exists: true })
                    {
                        try
                        {
                            File.Copy(result.FullName, mediaFileFullName, overwrite: true);
                            File.Delete(result.FullName);
                            state.OverlayApplied = true;
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
                        catch (Exception ex)
                        {
                            state.OverlayApplied = false;
                            state.PosterHash = null;
                            logger.LogError(ex, "Error updating overlay");
                            skippedBecauseOfError++;
                            continue;
                        }
                    }
                    else
                    {
                        logger.LogInformation("Could not apply overlay for {ItemPlexId} - {FriendlyTitle}", item.PlexId,
                            item.FriendlyTitle);
                        skippedBecauseOfError++;
                        state.OverlayApplied = false;
                    }
                }
                else
                {
                    // Already applied, update LastChecked
                    skippedOverlays++;
                    state.LastChecked = DateTime.UtcNow;
                }

                overlayStateManager.Upsert(state);
            }
        }

        logger.LogInformation(
            "Overlay operations completed with {RemovedOverlays} removed, {AppliedOverlays} applied, {SkippedOverlays} skipped, and {SkippedDueToError} error skips",
            removedOverlays, appliedOverlays, skippedOverlays, skippedBecauseOfError);
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

    private string GetOverlayText(OverlayManagerItem item)
    {
        var culture = new CultureInfo(_overlayConfiguration.Language);
        var formattedDate = item.ExpirationDate.ToString(_overlayConfiguration.DateFormat, culture);
        var overlayText = $"{_overlayConfiguration.OverlayText} {formattedDate}";
        if (_overlayConfiguration.EnableDaySuffix) overlayText += item.ExpirationDate.GetDaySuffix();
        if (_overlayConfiguration.EnableUppercase) overlayText = overlayText.ToUpper();
        return overlayText;
    }
}
