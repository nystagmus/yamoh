using System.Net;
using Ardalis.GuardClauses;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Clients;
using YAMOH.Models;

namespace YAMOH.Commands;

public class OverlayManagerCommand(
    ILogger<OverlayManagerCommand> logger,
    IOptions<YamohConfiguration> options,
    MaintainerrClient maintainerrClient,
    PlexAPI plexApi) : IYamohCommand
{
    private GetAllLibrariesResponse? _allLibraries = null;
    private List<OverlayManagerItem> _overlayManagerItems = [];

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
        // 1. Fetch collections/media items from Maintainerr
        // 2. For each media item:
        //    - Get Plex metadata
        //    - Build asset directory (if asset mode enabled)
        //    - Download poster from Plex if needed
        //    - Apply overlay
        //    - Save poster, backup original
        //    - Update state
        var collections = await maintainerrClient.GetCollections();

        if (collections.Count == 0)
        {
            logger.LogInformation("Maintainerr returned zero collections. Check configuration");
        }

        this._overlayManagerItems = [];

        foreach (var collection in collections)
        {
            if (!collection.IsActive) return;

            switch (collection.Type)
            {
                case (int)MaintainerrPlexDataType.Movies:
                    this._overlayManagerItems.AddRange(await GatherMovieCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Shows:
                    this._overlayManagerItems.AddRange(await GatherShowCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Seasons:
                    this._overlayManagerItems.AddRange(await GatherSeasonCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Episodes:
                    this._overlayManagerItems.AddRange(await GatherEpisodeCollectionItems(collection));
                    break;
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
                var libraryPath = sectionPaths.FirstOrDefault(x => mediaFilePath!.StartsWith(x));
                if (libraryPath is null)
                    return null;
                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));
                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Movies,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = mediaFilePath,
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
                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Shows,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
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
                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
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
                return new OverlayManagerItem()
                {
                    PlexId = maintainerrItem.PlexId,
                    DataType = MaintainerrPlexDataType.Episodes,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
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
