using System.Net;
using Ardalis.GuardClauses;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Clients;
using YAMOH.Infrastructure;
using YAMOH.Models;
using Directory = System.IO.Directory;

namespace YAMOH.Commands;

public class OverlayManagerCommand(
    ILogger<OverlayManagerCommand> logger,
    IOptions<YamohConfiguration> options,
    MaintainerrClient maintainerrClient,
    PlexClient plexClient,
    PlexAPI plexApi,
    OverlayStateManager overlayStateManager) : IYamohCommand
{
    private GetAllLibrariesResponse? _allLibraries = null;

    public string CommandName => "run";

    public string CommandDescription =>
        "Main function of the application. Manage overlays based on Maintainerr status.";

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

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var collections = await maintainerrClient.GetCollections();
        if (collections.Count == 0)
        {
            logger.LogInformation("Maintainerr returned zero collections. Check configuration");
            return;
        }

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

        // For each collection, process overlays and update state
        foreach (var collection in collections.Where(collection => collection.IsActive))
        {
            var items = collection.Type switch
            {
                (int)MaintainerrPlexDataType.Movies => await GatherMovieCollectionItems(collection),
                (int)MaintainerrPlexDataType.Shows => await GatherShowCollectionItems(collection),
                (int)MaintainerrPlexDataType.Seasons => await GatherSeasonCollectionItems(collection),
                (int)MaintainerrPlexDataType.Episodes => await GatherEpisodeCollectionItems(collection),
                _ => []
            };

            foreach (var item in items)
            {
                var state = overlayStateManager.GetByPlexId(item.PlexId.ToString());
                if (state is not { OverlayApplied: true })
                {
                    var assetBasePath = options.Value.AssetBasePath;
                    // Save original poster (Asset Mode)
                    var mediaFileFullPath = Path.Combine(assetBasePath, item.MediaFileRelativePath);
                    var mediaFileFullName = Path.Combine(mediaFileFullPath, item.MediaFileName);

                    if (!Directory.Exists(mediaFileFullPath))
                    {
                        Directory.CreateDirectory(mediaFileFullPath);
                    }

                    var fileList = Directory.GetFiles(mediaFileFullPath).Select(fileName => new FileInfo(fileName));

                    var originalPoster = fileList.FirstOrDefault(fileInfo => fileInfo.Exists && fileInfo.Name.StartsWith(item.MediaFileName) && fileInfo.IsImageByExtension());

                    if (originalPoster is { Exists: true })
                    {
                        var originalPosterBackupPath = mediaFileFullName + ".original" + originalPoster.Extension;
                        File.Copy(originalPoster.FullName, originalPosterBackupPath, overwrite: true);
                        mediaFileFullName = originalPoster.FullName;
                    }
                    else
                    {
                        // download original poster from plex instead
                        var plexPoster = await plexClient.DownloadPlexImageAsync(item.OriginalPlexPosterUrl);

                        if (plexPoster is { Exists: true } && plexPoster.IsImageByExtension())
                        {
                            var originalPosterBackupPath = mediaFileFullName + ".original" + plexPoster.Extension;
                            mediaFileFullName = mediaFileFullName + plexPoster.Extension;
                            File.Copy(plexPoster.FullName, mediaFileFullName, overwrite: true);
                            File.Copy(plexPoster.FullName, originalPosterBackupPath, overwrite: true);
                            File.Delete(plexPoster.FullName);
                        }
                        else
                        {
                            logger.LogInformation("Could not find or fetch original poster for {PlexId}", item.PlexId);
                        }

                    }
                    // Apply overlay (placeholder for actual overlay logic)
                    // ... ApplyOverlay(item.MediaFilePath) ...
                    // overlayStateManager.Upsert(new OverlayStateItem
                    // {
                    //     PlexId = item.PlexId.ToString(),
                    //     MaintainerrCollectionId = collection.Id,
                    //     PosterPath = mediaFileFullPath,
                    //     OriginalPosterPath = originalPosterBackupPath,
                    //     OverlayApplied = true,
                    //     LastChecked = DateTime.UtcNow,
                    //     PosterHash = null // Optionally compute hash
                    // });
                    // logger.LogInformation("Applied overlay and tracked state for PlexId {ItemPlexId}", item.PlexId);
                }
                else
                {
                    // Already applied, update LastChecked
                    state.LastChecked = DateTime.UtcNow;
                    overlayStateManager.Upsert(state);
                }
            }
        }
    }



    private async Task<IEnumerable<OverlayManagerItem>> GatherMovieCollectionItems(MaintainerrCollection collection)
    {
        return await GatherCollectionItems(
            collection,
            MaintainerrPlexDataType.Movies,
            async (metadataResponse, maintainerrItem) =>
            {
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
                    return null;
                var libraryPath = sectionPaths.FirstOrDefault(x => mediaFilePath.StartsWith(x));

                if (libraryPath is null)
                    return null;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                // clean up relative path
                mediaFilePath = mediaFilePath.Replace(libraryPath, string.Empty);

                if (Path.HasExtension(mediaFilePath))
                {
                    mediaFilePath = Guard.Against.NullOrWhiteSpace(Path.GetDirectoryName(mediaFilePath));
                }

                mediaFilePath = Path.Join(libraryName, mediaFilePath);

                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Movies,
                    LibraryName = libraryName,
                    MediaFileRelativePath = mediaFilePath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                };
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherShowCollectionItems(MaintainerrCollection collection)
    {
        return await GatherCollectionItems(
            collection,
            MaintainerrPlexDataType.Shows,
            async (metadataResponse, maintainerrItem) =>
            {
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
                    return null;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return null;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // Clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Shows,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                };
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherSeasonCollectionItems(MaintainerrCollection collection)
    {
        return await GatherCollectionItems(
            collection,
            MaintainerrPlexDataType.Seasons,
            async (metadataResponse, maintainerrItem) =>
            {
                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));
                var parentPlexId = Guard.Against.Null(plexMeta.Metadata[0].ParentRatingKey);

                var parentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = parentPlexId
                };
                var parentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(parentRequest);

                if (parentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return null;
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
                    return null;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return null;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                var seasonIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');

                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = string.Join("Season", seasonIndex)
                };
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherEpisodeCollectionItems(MaintainerrCollection collection)
    {
        return await GatherCollectionItems(
            collection,
            MaintainerrPlexDataType.Episodes,
            async (metadataResponse, maintainerrItem) =>
            {
                var plexMeta = Guard.Against.Null(metadataResponse.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));
                var grandParentPlexId = Guard.Against.Null(plexMeta.Metadata[0].GrandparentRatingKey);

                var grandparentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = grandParentPlexId
                };
                var grandparentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(grandparentRequest);

                if (grandparentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return null;
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
                    return null;
                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                    return null;

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                // clean up relative path
                showPath = showPath.Replace(libraryPath, string.Empty);
                showPath = Path.Join(libraryName, showPath);

                var seasonIndex = plexMeta.Metadata[0].ParentIndex.ToString()?.PadLeft(2, '0');
                var episodeIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');

                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Episodes,
                    LibraryName = libraryName,
                    MediaFileRelativePath = showPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = string.Join("S", seasonIndex, "E", episodeIndex)
                };
            });
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherCollectionItems(
        MaintainerrCollection collection,
        MaintainerrPlexDataType dataType,
        Func<GetMediaMetaDataResponse, MaintainerrMedia, Task<OverlayManagerItem?>> buildItem)
    {
        logger.LogInformation("Processing {DataType} collection: {Collection}", dataType, collection.Title);
        var items = new List<OverlayManagerItem>();
        var deleteAfterDays = collection.DeleteAfterDays;

        if (collection.Media == null || collection.Media.Count == 0)
        {
            logger.LogInformation("No media found for collection: {Collection}", collection.Title);
            return items;
        }

        foreach (var maintainerrItem in collection.Media)
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

                var item = await buildItem(metadataResponse, maintainerrItem);

                if (item != null)
                {
                    if (deleteAfterDays > 0)
                    {
                        var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                        item.HasExpiration = true;
                        item.ExpirationDate = addDate.AddDays(deleteAfterDays);
                    }

                    items.Add(item);

                    logger.LogInformation(
                        "Added {ItemType} item with PlexId: {PlexId}, LibraryName: {LibraryName}, ItemRelativePath: {ItemRelativePath}, Exp Date: {ExpirationDate}",
                        item.DataType, item.PlexId, item.LibraryName, item.MediaFileRelativePath, item.ExpirationDate);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }
}

