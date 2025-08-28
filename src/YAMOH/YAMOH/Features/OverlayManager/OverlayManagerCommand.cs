using System.Globalization;
using System.Net;
using Ardalis.GuardClauses;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Domain.Maintainerr;
using YAMOH.Domain.State;
using YAMOH.Infrastructure;
using YAMOH.Infrastructure.Configuration;
using YAMOH.Infrastructure.Extensions;
using YAMOH.Infrastructure.External;
using YAMOH.Infrastructure.ImageProcessing;

namespace YAMOH.Features.OverlayManager;

public class OverlayManagerCommand(
    ILogger<OverlayManagerCommand> logger,
    IOptions<YamohConfiguration> options,
    MaintainerrClient maintainerrClient,
    PlexClient plexClient,
    PlexAPI plexApi,
    OverlayHelper overlayHelper,
    OverlayStateManager overlayStateManager) : IYamohCommand
{
    private GetAllLibrariesResponse? _allLibraries;

    public string CommandName => "update-maintainerr-overlays";

    public string CommandDescription =>
        "Main function of the application. Manage overlays based on Maintainerr status.";

    private const string BackupFileNameSuffix = ".original";
    private const string KometaOverlayLabel = "Overlay";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var assetBasePath = options.Value.AssetBaseFullPath;

        if (!new DirectoryInfo(assetBasePath).HasWritePermissions())
        {
            logger.LogError("Asset Base path doesn't exist or is not writeable. Path: {Path}", assetBasePath);
            return;
        }

        var backupAssetBasePath = options.Value.BackupImageFullPath;

        if (!new DirectoryInfo(backupAssetBasePath).HasWritePermissions())
        {
            logger.LogError("Backup Image Path doesn't exist or is not writeable. Path: {Path}", backupAssetBasePath);
        }

        if (options.Value.RestoreOnly)
        {
            logger.LogWarning(
                "[yellow bold]Restore Only[/] was set in configuration. Will restore all posters back to original");
            var restoreItems = overlayStateManager.GetAppliedOverlays().ToList();

            var count = 0;

            foreach (var item in restoreItems)
            {
                count += await RestoreOriginalPoster(item);
            }

            foreach (var overlayStateItem in restoreItems)
            {
                overlayStateItem.OverlayApplied = false;
                overlayStateManager.Upsert(overlayStateItem);
            }

            logger.LogInformation("Restored {Count} images back to their originals", count);
            return;
        }

        var collections = (await maintainerrClient.GetCollections())
            .Where(x => x is { IsActive: true, DeleteAfterDays: > 0 }).ToList();

        if (collections.Count == 0)
        {
            logger.LogInformation("Maintainerr returned zero active collections. Check configuration");
            return;
        }

        var removedOverlays = await RestoreOriginalPosters(collections);
        var appliedOverlays = 0;
        var skippedOverlays = 0;
        var skippedBecauseOfError = 0;
        var overlaySettings = AddOverlaySettings.FromConfig(options);

        // For each collection, process overlays and update state
        foreach (var collection in collections.Where(collection => collection.IsActive))
        {
            var items = await GatherCollectionItems(collection);

            foreach (var item in items)
            {
                var state = overlayStateManager.GetByPlexId(item.PlexId);

                state ??= new OverlayStateItem
                {
                    PlexId = item.PlexId,
                    LibrarySectionId = item.LibraryId,
                    MaintainerrPlexType = item.MaintainerrPlexType,
                    IsChild = item.IsChild,
                    ParentPlexId = item.ParentPlexId,
                };

                var shouldReapply = state.LastKnownExpirationDate.Date != item.ExpirationDate.Date;

                if (options.Value.ReapplyOverlays || shouldReapply || state is not { OverlayApplied: true })
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
                    var originalPoster = GetOriginalPoster(backupFileDirectory, mediaFileDirectory, item);
                    var originalPosterBackupFullName = backupFileFullName + BackupFileNameSuffix;

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
                            logger.LogInformation("Could not find or fetch original poster for {PlexId}", item.PlexId);
                            continue;
                        }
                    }

                    state.PosterPath = mediaFileFullName;
                    state.OriginalPosterPath = originalPosterBackupFullName;
                    state.KometaLabelExists = item.KometaOverlayApplied;

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

                            if (item.KometaOverlayApplied)
                            {
                                if (!await plexClient.RemoveLabelKeyFromItem(item.LibraryId, item.PlexId, item.DataType,
                                        KometaOverlayLabel))
                                {
                                    logger.LogInformation(
                                        "Failed to remove label {KometaOverlayLabel} from PlexId: {PlexId}",
                                        KometaOverlayLabel, item.PlexId);
                                }

                                state.KometaLabelExists = false;
                            }

                            appliedOverlays++;

                            logger.LogInformation("Applied overlay and tracked state for PlexId {ItemPlexId}",
                                item.PlexId);
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
                        logger.LogInformation("Could not apply overlay for {ItemPlexId}", item.PlexId);
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

    private async Task<GetAllLibrariesResponse> GetAllLibraries()
    {
        var libraries = this._allLibraries ?? await plexApi.Library.GetAllLibrariesAsync();

        if (libraries is { StatusCode: (int)HttpStatusCode.OK, Object.MediaContainer.Directory: not null })
        {
            this._allLibraries = libraries;
            return libraries;
        }

        logger.LogInformation("Skipping Plex Operations: Could not fetch Plex Library metadata");
        throw new InvalidOperationException("Plex Library metadata could not be retrieved");
    }

    private async Task<int> RestoreOriginalPosters(List<MaintainerrCollection> collections)
    {
        // Get all PlexIds currently in Maintainerr
        var currentPlexIds = collections.SelectMany(c => c.Media ?? []).Select(m => m.PlexId).ToHashSet();

        // Restore overlays for items no longer in Maintainerr but still in Plex
        var pendingRestores = overlayStateManager.GetPendingRestores(currentPlexIds);
        var count = 0;

        foreach (var pendingRestore in pendingRestores)
        {
            count += await RestoreOriginalPoster(pendingRestore);
        }

        return count;
    }

    private async Task<int> RestoreOriginalPoster(OverlayStateItem stateItem)
    {
        // Restore original poster
        if (File.Exists(stateItem.OriginalPosterPath))
        {
            File.Copy(stateItem.OriginalPosterPath, stateItem.PosterPath, overwrite: true);
            File.Delete(stateItem.OriginalPosterPath);

            if (!await plexClient.RemoveLabelKeyFromItem(stateItem.LibrarySectionId, stateItem.PlexId,
                    stateItem.MaintainerrPlexType,
                    KometaOverlayLabel))
            {
                logger.LogInformation("Failed to remove label {KometaOverlayLabel} from PlexId: {PlexId}",
                    KometaOverlayLabel, stateItem.PlexId);
            }

            overlayStateManager.Remove(stateItem.PlexId);
            logger.LogInformation("Restored original poster for PlexId {StateItemPlexId}", stateItem.PlexId);
            return 1;
        }

        logger.LogWarning("Original poster backup missing for PlexId {StateItemPlexId}", stateItem.PlexId);
        return 0;
    }

    private string GetOverlayText(OverlayManagerItem item)
    {
        var culture = new CultureInfo(options.Value.Language);
        var formattedDate = item.ExpirationDate.ToString(options.Value.DateFormat, culture);
        var overlayText = $"{options.Value.OverlayText} {formattedDate}";
        if (options.Value.EnableDaySuffix) overlayText += item.ExpirationDate.GetDaySuffix();
        if (options.Value.EnableUppercase) overlayText = overlayText.ToUpper();
        return overlayText;
    }

    private static FileInfo? GetOriginalPoster(DirectoryInfo backupDirectory, DirectoryInfo mediaFileDirectory,
        OverlayManagerItem item)
    {
        var backupFileList = backupDirectory.GetFiles();

        var backupMatches = backupFileList.Where(fileInfo => fileInfo.Exists &&
                                                             fileInfo.Name.StartsWith(item.MediaFileName) &&
                                                             fileInfo.IsImageByExtension())
            .ToList();
        var fileList = mediaFileDirectory.GetFiles();
        var backupOriginalPoster = backupMatches.FirstOrDefault(x => x.Name.Contains(BackupFileNameSuffix));

        var matches = fileList.Where(fileInfo => fileInfo.Exists &&
                                                 fileInfo.Name.StartsWith(item.MediaFileName) &&
                                                 fileInfo.IsImageByExtension())
            .ToList();
        return backupOriginalPoster ?? matches.FirstOrDefault();
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherCollectionItems(MaintainerrCollection collection)
    {
        if (collection.Media == null)
        {
            return new List<OverlayManagerItem>();
        }

        var maintainerrMedia = collection.Media
            .Select(x => new MaintainerrMediaDto() { AddDate = x.AddDate, PlexId = x.PlexId })
            .ToList();

        var items = collection.Type switch
        {
            (int)MaintainerrPlexDataType.Movies => await GatherMovieCollectionItems(collection.Title,
                collection.DeleteAfterDays, maintainerrMedia),
            (int)MaintainerrPlexDataType.Shows => await GatherShowCollectionItems(collection.Title,
                collection.DeleteAfterDays, maintainerrMedia),
            (int)MaintainerrPlexDataType.Seasons => await GatherSeasonCollectionItems(collection.Title,
                collection.DeleteAfterDays, maintainerrMedia),
            (int)MaintainerrPlexDataType.Episodes => await GatherEpisodeCollectionItems(collection.Title,
                collection.DeleteAfterDays, maintainerrMedia),
            _ => []
        };
        return items;
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherMovieCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Movies,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild: false,
            async (metadataResponse, plexId, _) =>
            {
                var items = new List<OverlayManagerItem>();

                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));

                var mediaFilePath = Guard.Against.Null(plexMeta.Metadata[0].Media?[0].Part?[0].File,
                    nameof(GetMediaMetaDataPart.File));

                var librarySectionId = Guard.Against.Null(plexMeta.Metadata[0].LibrarySectionID,
                    nameof(GetMediaMetaDataMetadata.LibrarySectionID));
                var libraries = await GetAllLibraries();

                var directories = Guard.Against.Null(libraries.Object?.MediaContainer?.Directory,
                    nameof(GetAllLibrariesMediaContainer.Directory));

                var sectionPaths = directories
                    .FirstOrDefault(x => x.Key == librarySectionId.ToString())?.Location.Select(p => p.Path)
                    .ToList();

                if (sectionPaths is null || sectionPaths.Count == 0)
                    return items;
                var libraryPath = sectionPaths.FirstOrDefault(x => mediaFilePath.StartsWith(x));

                if (libraryPath is null)
                    return items;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                // clean up relative path
                mediaFilePath = mediaFilePath.Replace(libraryPath, string.Empty);

                if (Path.HasExtension(mediaFilePath))
                {
                    mediaFilePath = Guard.Against.NullOrWhiteSpace(Path.GetDirectoryName(mediaFilePath));
                }

                mediaFilePath = Path.Join(libraryName, mediaFilePath);

                items.Add(new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Movies,
                    LibraryName = libraryName,
                    LibraryId = librarySectionId,
                    MaintainerrPlexType = MaintainerrPlexDataType.Movies,
                    MediaFileRelativePath = mediaFilePath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                });
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherShowCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Shows,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild: false,
            async (metadataResponse, plexId, addDate) =>
            {
                var items = new List<OverlayManagerItem>();

                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));
                var showPath = Guard.Against.Null(metadataResponse.Object.MediaContainer.Metadata[0].Location?[0].Path);

                var librarySectionId = Guard.Against.Null(plexMeta.Metadata[0].LibrarySectionID,
                    nameof(GetMediaMetaDataMetadata.LibrarySectionID));
                var libraries = await GetAllLibraries();

                var directories = Guard.Against.Null(libraries.Object?.MediaContainer?.Directory,
                    nameof(GetAllLibrariesMediaContainer.Directory));

                var sectionPaths = directories
                    .FirstOrDefault(x => x.Key == librarySectionId.ToString())?.Location.Select(p => p.Path)
                    .ToList();

                if (sectionPaths is null || sectionPaths.Count == 0)
                    return items;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return items;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // Clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                items.Add(new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Shows,
                    LibraryName = libraryName,
                    LibraryId = librarySectionId,
                    MaintainerrPlexType = MaintainerrPlexDataType.Shows,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                });

                if (!options.Value.OverlaySeasonEpisodes)
                {
                    return items;
                }

                logger.LogInformation("Adding Seasons for {PlexId}", plexId);
                var episodeInfos = await plexClient.GetMetadataChildrenAsync(plexId);

                var episodeIds = episodeInfos?.MediaContainer?.Metadata?.Where(x => x.RatingKey != null)
                    .Select(metadata =>
                        int.Parse(metadata.RatingKey!)).ToArray();

                if (episodeIds is null || episodeIds.Length <= 0)
                {
                    return items;
                }

                var childrenMaintainerrMedia = episodeIds
                    .Select(x => new MaintainerrMediaDto() { PlexId = x, AddDate = addDate })
                    .ToList();

                items.AddRange(await GatherSeasonCollectionItems(collectionTitle, deleteAfterDays,
                    childrenMaintainerrMedia, true, plexId));

                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherSeasonCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild = false,
        int? parentId = null)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Seasons,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild,
            async (metadataResponse, plexId, addDate) =>
            {
                var items = new List<OverlayManagerItem>();

                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));
                var parentPlexId = Guard.Against.Null(plexMeta.Metadata[0].ParentRatingKey);

                var parentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = parentPlexId
                };
                var parentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(parentRequest);

                if (parentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return items;
                var parentMetadata = Guard.Against.Null(parentMetadataResponse.Object?.MediaContainer);
                var showPath = Guard.Against.Null(parentMetadata.Metadata[0].Location?[0].Path);

                var librarySectionId = Guard.Against.Null(plexMeta.Metadata[0].LibrarySectionID,
                    nameof(GetMediaMetaDataMetadata.LibrarySectionID));
                var libraries = await GetAllLibraries();

                var directories = Guard.Against.Null(libraries.Object?.MediaContainer?.Directory,
                    nameof(GetAllLibrariesMediaContainer.Directory));

                var sectionPaths = directories
                    .FirstOrDefault(x => x.Key == librarySectionId.ToString())?.Location.Select(p => p.Path)
                    .ToList();

                if (sectionPaths is null || sectionPaths.Count == 0)
                    return items;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return items;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                var seasonIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');

                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    LibraryId = librarySectionId,
                    MaintainerrPlexType = MaintainerrPlexDataType.Seasons,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = $"Season{seasonIndex}"
                };

                if (asChild)
                {
                    item.IsChild = true;
                    item.ParentPlexId = parentId;
                }

                items.Add(item);

                if (!options.Value.OverlaySeasonEpisodes)
                {
                    return items;
                }

                logger.LogInformation("Adding Episodes for {PlexId}", plexId);
                var episodeInfos = await plexClient.GetMetadataChildrenAsync(plexId);

                var episodeIds = episodeInfos?.MediaContainer?.Metadata?.Where(x => x.RatingKey != null)
                    .Select(metadata =>
                        int.Parse(metadata.RatingKey!)).ToArray();

                if (episodeIds is null || episodeIds.Length <= 0)
                {
                    return items;
                }

                var childrenMaintainerrMedia = episodeIds
                    .Select(x => new MaintainerrMediaDto() { PlexId = x, AddDate = addDate })
                    .ToList();

                items.AddRange(await GatherEpisodeCollectionItems(collectionTitle, deleteAfterDays,
                    childrenMaintainerrMedia, true, plexId));

                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherEpisodeCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild = false,
        int? parentId = null)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Episodes,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild,
            async (metadataResponse, plexId, _) =>
            {
                var items = new List<OverlayManagerItem>();

                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));
                var grandParentPlexId = Guard.Against.Null(plexMeta.Metadata[0].GrandparentRatingKey);

                var grandparentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = grandParentPlexId
                };
                var grandparentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(grandparentRequest);

                if (grandparentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return items;

                var grandparentMetadata = Guard.Against.Null(grandparentMetadataResponse.Object?.MediaContainer);
                var showPath = Guard.Against.Null(grandparentMetadata.Metadata[0].Location?[0].Path);

                var librarySectionId = Guard.Against.Null(plexMeta.Metadata[0].LibrarySectionID,
                    nameof(GetMediaMetaDataMetadata.LibrarySectionID));
                var libraries = await GetAllLibraries();

                var directories = Guard.Against.Null(libraries.Object?.MediaContainer?.Directory,
                    nameof(GetAllLibrariesMediaContainer.Directory));

                var sectionPaths = directories
                    .FirstOrDefault(x => x.Key == librarySectionId.ToString())?.Location.Select(p => p.Path)
                    .ToList();

                if (sectionPaths is null || sectionPaths.Count == 0)
                    return items;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return items;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                var seasonIndex = plexMeta.Metadata[0].ParentIndex.ToString()?.PadLeft(2, '0');
                var episodeIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');

                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Episodes,
                    LibraryName = libraryName,
                    LibraryId = librarySectionId,
                    MaintainerrPlexType = MaintainerrPlexDataType.Episodes,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = $"S{seasonIndex}E{episodeIndex}"
                };

                if (asChild)
                {
                    item.IsChild = true;
                    item.ParentPlexId = parentId;
                }

                items.Add(item);
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherCollectionItems(
        MaintainerrPlexDataType dataType,
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild,
        Func<GetMediaMetaDataResponse, int, DateTime, Task<IEnumerable<OverlayManagerItem>>> buildItems)
    {
        logger.LogInformation("Processing {DataType} collection: {Collection}", dataType, collectionTitle);
        var items = new List<OverlayManagerItem>();

        if (maintainerrMedia.Count == 0)
        {
            logger.LogInformation("No media found for collection: {Collection}", collectionTitle);
            return items;
        }

        foreach (var maintainerrItem in maintainerrMedia)
        {
            var plexId = maintainerrItem.PlexId;
            logger.LogInformation("Fetching Plex Metadata for {PlexId}", plexId);

            try
            {
                var request = new GetMediaMetaDataRequest()
                {
                    RatingKey = plexId.ToString()
                };
                var metadataResponse = await plexApi.Library.GetMediaMetaDataAsync(request);

                if (metadataResponse.StatusCode != (int)HttpStatusCode.OK)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because Metadata response was status code: {StatusCode}", plexId,
                        metadataResponse.StatusCode);
                    continue;
                }

                var builtItems = await buildItems(metadataResponse, maintainerrItem.PlexId, maintainerrItem.AddDate);

                foreach (var builtItem in builtItems)
                {
                    if (deleteAfterDays > 0)
                    {
                        var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                        builtItem.HasExpiration = true;
                        builtItem.ExpirationDate = addDate.AddDays(deleteAfterDays);
                    }

                    // Get labels
                    var labels = await plexClient.GetLabelsForPlexIdAsync(plexId);

                    if (labels != null)
                    {
                        builtItem.KometaOverlayApplied = labels.Any(x =>
                            x.Tag != null && x.Tag.Equals(KometaOverlayLabel, StringComparison.OrdinalIgnoreCase));
                    }

                    items.Add(builtItem);

                    if (!asChild)
                        logger.LogInformation(
                            "Added {ItemType} item with PlexId: {PlexId}, LibraryName: {LibraryName}, ItemRelativePath: {ItemRelativePath}, Exp Date: {ExpirationDate}",
                            builtItem.DataType, builtItem.PlexId, builtItem.LibraryName,
                            builtItem.MediaFileRelativePath,
                            builtItem.ExpirationDate);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }

    private class MaintainerrMediaDto
    {
        public required int PlexId { get; init; }
        public required DateTime AddDate { get; init; }
    }
}
