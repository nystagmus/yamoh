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
    private YamohConfiguration _config = options.Value;
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

        var items = new List<OverlayManagerItem>();

        foreach (var collection in collections)
        {
            if (!collection.IsActive) return;

            switch (collection.Type)
            {
                case (int)MaintainerrPlexDataType.Movies:
                    items.AddRange(await GatherMovieCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Shows:
                    items.AddRange(await GatherShowCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Seasons:
                    items.AddRange(await GatherSeasonCollectionItems(collection));
                    break;
                case (int)MaintainerrPlexDataType.Episodes:
                    items.AddRange(await GatherEpisodeCollectionItems(collection));
                    break;
            }
        }
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherMovieCollectionItems(MaintainerrCollection collection)
    {
        logger.LogInformation("Processing Movie collection: {Collection}", collection.Title);
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
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined from PlexAPI",
                        plexId);
                    continue;
                }

                var libraryPath = sectionPaths.FirstOrDefault(x => mediaFilePath!.StartsWith(x));

                if (libraryPath is null)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined", plexId);
                    continue;
                }

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                logger.LogInformation(
                    "Found Metadata for Plex ID: {PlexId}, Library: {LibraryName}, Library Folder: {@LibraryFolder}, Media File: {MediaFile}",
                    plexId, libraryName, libraryPath, mediaFilePath);


                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Movies,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = mediaFilePath,
                };

                if (deleteAfterDays > 0)
                {
                    var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                    item.HasExpiration = true;
                    item.ExpirationDate = addDate.AddDays(deleteAfterDays);
                }
                items.Add(item);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherShowCollectionItems(MaintainerrCollection collection)
    {
        logger.LogInformation("Processing TV Show collection: {Collection}", collection.Title);
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
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined from PlexAPI",
                        plexId);
                    continue;
                }

                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined", plexId);
                    continue;
                }

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                logger.LogInformation(
                    "Found Metadata for Plex ID: {PlexId}, Library: {LibraryName}, Library Folder: {@LibraryFolder}, Media File: {MediaFile}",
                    plexId, libraryName, libraryPath, showPath);

                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Shows,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
                };

                if (deleteAfterDays > 0)
                {
                    var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                    item.HasExpiration = true;
                    item.ExpirationDate = addDate.AddDays(deleteAfterDays);
                }
                items.Add(item);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherSeasonCollectionItems(MaintainerrCollection collection)
    {
        logger.LogInformation("Processing TV Season collection: {Collection}", collection.Title);
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
                var metadata = await plexApi.Library.GetMediaMetaDataAsync(request);

                if (metadata.StatusCode != (int)HttpStatusCode.OK)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because Metadata response was status code: {StatusCode}", plexId,
                        metadata.StatusCode);
                    continue;
                }

                var plexMeta = Guard.Against.Null(metadata.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));

                var parentPlexId = Guard.Against.Null(plexMeta.Metadata[0].ParentRatingKey);

                var parentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = parentPlexId
                };
                var parentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(parentRequest);
                if (parentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because Metadata response was status code: {StatusCode}", plexId,
                        metadata.StatusCode);
                    continue;
                }

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
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined from PlexAPI",
                        plexId);
                    continue;
                }

                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined", plexId);
                    continue;
                }

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                logger.LogInformation(
                    "Found Metadata for Plex ID: {PlexId}, Library: {LibraryName}, Library Folder: {@LibraryFolder}, Media File: {MediaFile}",
                    plexId, libraryName, libraryPath, showPath);

                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
                };

                if (deleteAfterDays > 0)
                {
                    var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                    item.HasExpiration = true;
                    item.ExpirationDate = addDate.AddDays(deleteAfterDays);
                }
                items.Add(item);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }

    private async Task<IEnumerable<OverlayManagerItem>> GatherEpisodeCollectionItems(MaintainerrCollection collection)
    {
        logger.LogInformation("Processing Episodes collection: {Collection}", collection.Title);
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
                var metadata = await plexApi.Library.GetMediaMetaDataAsync(request);

                if (metadata.StatusCode != (int)HttpStatusCode.OK)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because Metadata response was status code: {StatusCode}", plexId,
                        metadata.StatusCode);
                    continue;
                }

                var plexMeta = Guard.Against.Null(metadata.Object?.MediaContainer,
                    nameof(GetMediaMetaDataResponse.Object));

                var grandParentPlexId = Guard.Against.Null(plexMeta.Metadata[0].GrandparentRatingKey);

                var grandparentRequest = new GetMediaMetaDataRequest()
                {
                    RatingKey = grandParentPlexId
                };
                var grandparentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(grandparentRequest);
                if (grandparentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because Metadata response was status code: {StatusCode}", plexId,
                        metadata.StatusCode);
                    continue;
                }

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
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined from PlexAPI",
                        plexId);
                    continue;
                }

                var libraryPath = sectionPaths.FirstOrDefault(x => showPath.StartsWith(x));

                if (libraryPath is null)
                {
                    logger.LogInformation(
                        "Skipping Plex ID {PlexId} because library directory path could not be determined", plexId);
                    continue;
                }

                var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
                    nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

                logger.LogInformation(
                    "Found Metadata for Plex ID: {PlexId}, Library: {LibraryName}, Library Folder: {@LibraryFolder}, Media File: {MediaFile}",
                    plexId, libraryName, libraryPath, showPath);

                var item = new OverlayManagerItem()
                {
                    PlexId = plexId,
                    DataType = MaintainerrPlexDataType.Seasons,
                    LibraryName = libraryName,
                    LibraryPath = libraryPath,
                    MediaFilePath = showPath,
                };

                if (deleteAfterDays > 0)
                {
                    var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                    item.HasExpiration = true;
                    item.ExpirationDate = addDate.AddDays(deleteAfterDays);
                }
                items.Add(item);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Skipping {PlexId} due to error", plexId);
            }
        }

        return items;
    }
}
