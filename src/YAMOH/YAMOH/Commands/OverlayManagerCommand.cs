using System.Globalization;
using System.Net;
using Ardalis.GuardClauses;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Clients;
using YAMOH.Infrastructure;
using YAMOH.Models;

namespace YAMOH.Commands;

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

    public string CommandName => "run";

    public string CommandDescription =>
        "Main function of the application. Manage overlays based on Maintainerr status.";

    private const string BackupFileNameSuffix = ".original";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var collections = await maintainerrClient.GetCollections();
        if (collections.Count == 0)
        {
            logger.LogInformation("Maintainerr returned zero collections. Check configuration");
            return;
        }

        RestoreOriginalPosters(collections);

        // For each collection, process overlays and update state
        foreach (var collection in collections.Where(collection => collection.IsActive))
        {
            var items = await GatherCollectionItems(collection);

            foreach (var item in items)
            {
                var state = overlayStateManager.GetByPlexId(item.PlexId.ToString());
                state ??= new OverlayStateItem
                {
                    PlexId = item.PlexId.ToString(),
                };

                var shouldReapply = state.LastKnownExpirationDate.Date != item.ExpirationDate.Date;

                if (options.Value.ReapplyOverlays || shouldReapply || state is not { OverlayApplied: true })
                {
                    state.MaintainerrCollectionId = collection.Id;
                    state.LastChecked = DateTimeOffset.UtcNow;
                    state.LastKnownExpirationDate = item.ExpirationDate;

                    var assetBasePath = options.Value.AssetBasePath;
                    // Save original poster (Asset Mode)
                    var mediaFileFullPath = Path.GetFullPath(Path.Combine(assetBasePath, item.MediaFileRelativePath));
                    var mediaFileFullName = Path.GetFullPath(Path.Combine(mediaFileFullPath, item.MediaFileName));
                    var mediaFileDirectory = new DirectoryInfo(mediaFileFullPath);

                    if (!mediaFileDirectory.Exists)
                    {
                        mediaFileDirectory.Create();
                    }

                    // Will get poster.original.jpg first, else poster.jpg if exists, or null
                    var originalPoster = GetOriginalPoster(mediaFileDirectory, item);
                    var originalPosterBackupFullName = mediaFileFullName + BackupFileNameSuffix;
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
                            logger.LogInformation("Could not find or fetch original poster for {PlexId}", item.PlexId);
                            continue;
                        }
                    }

                    state.PosterPath = mediaFileFullName;
                    state.OriginalPosterPath = originalPosterBackupFullName;

                    // Apply overlay
                    var overlayText = GetOverlayText(item);

                    var result = overlayHelper.AddOverlay(item.PlexId, mediaFileFullName, overlayText);

                    if (result is { Exists: true })
                    {
                        File.Copy(result.FullName, mediaFileFullName, overwrite: true);
                        File.Delete(result.FullName);
                        state.OverlayApplied = true;
                        state.PosterHash = null;
                        logger.LogInformation("Applied overlay and tracked state for PlexId {ItemPlexId}", item.PlexId);
                    }
                    else
                    {
                        logger.LogInformation("Could not apply overlay for {ItemPlexId}", item.PlexId);
                        state.OverlayApplied = false;
                    }
                }
                else
                {
                    // Already applied, update LastChecked
                    state.LastChecked = DateTime.UtcNow;
                }

                overlayStateManager.Upsert(state);
            }
        }
    }

    private async Task<GetAllLibrariesResponse> GetAllLibraries()
    {
        var libraries = _allLibraries ?? await plexApi.Library.GetAllLibrariesAsync();

        if (libraries is { StatusCode: (int)HttpStatusCode.OK, Object.MediaContainer.Directory: not null })
        {
            _allLibraries = libraries;
            return libraries;
        }

        logger.LogInformation("Skipping Plex Operations: Could not fetch Plex Library metadata");
        throw new InvalidOperationException("Plex Library metadata could not be retrieved");
    }

    private void RestoreOriginalPosters(List<MaintainerrCollection> collections)
    {
        // Get all PlexIds currently in Maintainerr
        var currentPlexIds = collections.SelectMany(c => c.Media ?? []).Select(m => m.PlexId.ToString()).ToHashSet();

        // Restore overlays for items no longer in Maintainerr but still in Plex
        foreach (var stateItem in overlayStateManager.GetPendingRestores(currentPlexIds))
        {
            // Restore original poster
            if (File.Exists(stateItem.OriginalPosterPath))
            {
                File.Copy(stateItem.OriginalPosterPath, stateItem.PosterPath, overwrite: true);
                overlayStateManager.Remove(stateItem.PlexId);
                logger.LogInformation("Restored original poster for PlexId {StateItemPlexId}", stateItem.PlexId);
            }
            else
            {
                logger.LogWarning("Original poster backup missing for PlexId {StateItemPlexId}", stateItem.PlexId);
            }
        }
    }

    private string GetOverlayText(OverlayManagerItem item)
    {
        var culture = new CultureInfo(options.Value.Language);
        var formattedDate = item.ExpirationDate.ToString(options.Value.DateFormat, culture);
        var overlayText = $"{options.Value.OverlayText} {formattedDate}";
        if(options.Value.EnableDaySuffix) overlayText += item.ExpirationDate.GetDaySuffix();
        if(options.Value.EnableUppercase) overlayText = overlayText.ToUpper();
        return overlayText;
    }

    private static FileInfo? GetOriginalPoster(DirectoryInfo mediaFileDirectory, OverlayManagerItem item)
    {
        var fileList = mediaFileDirectory.GetFiles();

        var matches = fileList.Where(fileInfo => fileInfo.Exists &&
                                                                 fileInfo.Name.StartsWith(item.MediaFileName) &&
                                                                 fileInfo.IsImageByExtension())
            .ToList();
        var backupOriginalPoster = matches.FirstOrDefault(x => x.Name.Contains(BackupFileNameSuffix));
        return backupOriginalPoster ?? matches.FirstOrDefault();
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherCollectionItems(MaintainerrCollection collection)
    {
        if (collection.Media == null)
        {
            return new List<OverlayManagerItem>();
        }
        var dtos = collection.Media.Select(x => new MaintainerrMediaDto() { AddDate = x.AddDate, PlexId = x.PlexId }).ToList();
        var items = collection.Type switch
        {
            (int)MaintainerrPlexDataType.Movies => await GatherMovieCollectionItems(collection.Title, collection.DeleteAfterDays, dtos),
            (int)MaintainerrPlexDataType.Shows => await GatherShowCollectionItems(collection.Title, collection.DeleteAfterDays, dtos),
            (int)MaintainerrPlexDataType.Seasons => await GatherSeasonCollectionItems(collection.Title, collection.DeleteAfterDays, dtos),
            (int)MaintainerrPlexDataType.Episodes => await GatherEpisodeCollectionItems(collection.Title, collection.DeleteAfterDays, dtos),
            _ => []
        };
        return items;
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherMovieCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> dtos)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Movies,
            collectionTitle,
            deleteAfterDays,
            dtos,
            async (metadataResponse, plexId) =>
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
                    MediaFileRelativePath = mediaFilePath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                });
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherShowCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> dtos)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Shows,
            collectionTitle,
            deleteAfterDays,
            dtos,
            async (metadataResponse, plexId) =>
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

                // todo: handle children? include option & call GatherSeasonCollectionItems for children
                items.Add(new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Shows,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                });
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherSeasonCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> dtos)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Seasons,
            collectionTitle,
            deleteAfterDays,
            dtos,
            async (metadataResponse, plexId) =>
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

                // todo: handle children? include option & call GatherEpisodeCollectionItems for children
                items.Add(new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = $"Season{seasonIndex}"
                });
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherEpisodeCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> dtos)
    {
        return await GatherCollectionItems(
            MaintainerrPlexDataType.Episodes,
            collectionTitle,
            deleteAfterDays,
            dtos,
            async (metadataResponse, plexId) =>
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

                items.Add(new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Episodes,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = $"S{seasonIndex}E{episodeIndex}"
                });
                return items;
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherCollectionItems(
        MaintainerrPlexDataType dataType,
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> dtos,
        Func<GetMediaMetaDataResponse, int, Task<IEnumerable<OverlayManagerItem>>> buildItems)
    {
        logger.LogInformation("Processing {DataType} collection: {Collection}", dataType, collectionTitle);
        var items = new List<OverlayManagerItem>();
        //var deleteAfterDays = collection.DeleteAfterDays;

        if (dtos.Count == 0)
        {
            logger.LogInformation("No media found for collection: {Collection}", collectionTitle);
            return items;
        }

        foreach (var maintainerrItem in dtos)
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

                var builtItems = await buildItems(metadataResponse, maintainerrItem.PlexId);

                foreach (var builtItem in builtItems)
                {
                    if (deleteAfterDays > 0)
                    {
                        var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                        builtItem.HasExpiration = true;
                        builtItem.ExpirationDate = addDate.AddDays(deleteAfterDays);
                    }

                    items.Add(builtItem);

                    logger.LogInformation(
                        "Added {ItemType} item with PlexId: {PlexId}, LibraryName: {LibraryName}, ItemRelativePath: {ItemRelativePath}, Exp Date: {ExpirationDate}",
                        builtItem.DataType, builtItem.PlexId, builtItem.LibraryName, builtItem.MediaFileRelativePath, builtItem.ExpirationDate);
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
        public required int PlexId { get; set; }
        public required DateTime AddDate { get; set; }
    }
}

