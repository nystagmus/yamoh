using Ardalis.GuardClauses;
using LukeHagar.PlexAPI.SDK;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Yamoh.Domain.Maintainerr;
using Yamoh.Infrastructure.Configuration;
using Yamoh.Infrastructure.External;

namespace Yamoh.Features.OverlayManager;

public class PlexMetadataBuilder(
    ILogger<PlexMetadataBuilder> logger,
    IOptions<OverlayBehaviorConfiguration> overlayBehaviorConfigurationOptions,
    PlexClient plexClient,
    PlexAPI plexApi)
{
    private readonly OverlayBehaviorConfiguration _overlayBehaviorConfiguration =
        overlayBehaviorConfigurationOptions.Value;

    private GetAllLibrariesResponse? _allLibraries;

    public async Task<IEnumerable<PlexMetadataBuilderItem>> BuildFromMaintainerrCollection(IMaintainerrCollectionResponse collection)
    {
        // update to exclude collections without a delete action set. Could possibly expand this in future to do other things
        // with collections that are used for other purposes
        if (collection.Media is null || collection.DeleteAfterDays is null)
        {
            return [];
        }

        var deleteAfterDays = collection.DeleteAfterDays.Value;

        var maintainerrMedia = collection.Media
            .Where(x => x.MediaServerId is not null)
            .Select(x => new MaintainerrMediaDto(x.MediaServerId!, x.AddDate))
            .ToList();

        try
        {
            var items = collection.Type switch
            {
                MaintainerrDataType.Movies => await GatherMovieCollectionItems(collection.Title,
                    deleteAfterDays, maintainerrMedia),
                MaintainerrDataType.Shows => await GatherShowCollectionItems(collection.Title,
                    deleteAfterDays, maintainerrMedia),
                MaintainerrDataType.Seasons => await GatherSeasonCollectionItems(collection.Title,
                    deleteAfterDays, maintainerrMedia),
                MaintainerrDataType.Episodes => await GatherEpisodeCollectionItems(collection.Title,
                    deleteAfterDays, maintainerrMedia),
                _ => []
            };
            return items;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception encountered when attempting to hydrate metadata for {CollectionName}", collection.Title);
        }

        return new List<PlexMetadataBuilderItem>();
    }

    private async Task<IEnumerable<PlexMetadataBuilderItem>> GatherMovieCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia)
    {
        return await GatherCollectionItems(
            MaintainerrDataType.Movies,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild: false,
            async (plexMeta, plexId, _) =>
            {
                var items = new List<PlexMetadataBuilderItem>();

                var friendlyTitle = Guard.Against.Null(plexMeta.Metadata[0].Title);

                var mediaFilePath = Guard.Against.Null(plexMeta.Metadata[0].Media?[0].Part?[0].File,
                    nameof(GetMediaMetaDataPart.File));

                var libraryInfo = await GetPlexItemLibraryInfoAsync(plexMeta, mediaFilePath);
                if (libraryInfo == null) return new List<PlexMetadataBuilderItem>();

                // clean up relative path
                mediaFilePath = mediaFilePath.Replace(libraryInfo.LibraryPath, string.Empty);

                if (Path.HasExtension(mediaFilePath))
                {
                    mediaFilePath = Guard.Against.NullOrWhiteSpace(Path.GetDirectoryName(mediaFilePath));
                }

                mediaFilePath = Path.Join(libraryInfo.LibraryName, mediaFilePath);

                items.Add(new PlexMetadataBuilderItem
                {
                    PlexId = plexId,
                    FriendlyTitle = friendlyTitle,
                    DataType = MaintainerrDataType.Movies,
                    LibraryName = libraryInfo.LibraryName,
                    LibraryId = (int)libraryInfo.LibrarySectionId,
                    MediaFileRelativePath = mediaFilePath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                });
                return items;
            });
    }

    private async Task<IEnumerable<PlexMetadataBuilderItem>> GatherShowCollectionItems(string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia)
    {
        return await GatherCollectionItems(
            MaintainerrDataType.Shows,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild: false,
            async (plexMeta, plexId, addDate) =>
            {
                var friendlyTitle = Guard.Against.Null(plexMeta.Metadata[0].Title);

                var showPath = Guard.Against.Null(plexMeta.Metadata[0].Location?[0].Path);

                var libraryInfo = await GetPlexItemLibraryInfoAsync(plexMeta, showPath);
                if (libraryInfo == null) return new List<PlexMetadataBuilderItem>();

                var item = new PlexMetadataBuilderItem
                {
                    PlexId = plexId,
                    FriendlyTitle = friendlyTitle,
                    DataType = MaintainerrDataType.Shows,
                    LibraryName = libraryInfo.LibraryName,
                    LibraryId = (int)libraryInfo.LibrarySectionId,
                    MediaFileRelativePath = libraryInfo.CleanTvShowPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = "poster",
                };

                var items = new List<PlexMetadataBuilderItem> { item };

                if (!this._overlayBehaviorConfiguration.OverlayShowSeasons)
                {
                    return items;
                }

                // Get Child Seasons
                logger.LogInformation("Including Metadata for Seasons for {PlexId} - {FriendlyTitle}", plexId,
                    friendlyTitle);
                var episodeInfos = await plexClient.GetMetadataChildrenAsync(plexId);

                var episodeIds = episodeInfos?.MediaContainer?.Metadata?.Where(x => x.RatingKey != null)
                    .Select(metadata =>
                        int.Parse(metadata.RatingKey!)).ToArray();

                if (episodeIds is null || episodeIds.Length <= 0)
                {
                    return items;
                }

                var childrenMaintainerrMedia = episodeIds
                    .Select(x => new MaintainerrMediaDto(x.ToString(), addDate))
                    .ToList();

                items.AddRange(await GatherSeasonCollectionItems(collectionTitle, deleteAfterDays,
                    childrenMaintainerrMedia, true, plexId));

                return items;
            });
    }

    private async Task<IEnumerable<PlexMetadataBuilderItem>> GatherSeasonCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild = false,
        string? parentId = null)
    {
        return await GatherCollectionItems(
            MaintainerrDataType.Seasons,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild,
            async (plexMeta, plexId, addDate) =>
            {
                var friendlyTitle = Guard.Against.Null(plexMeta.Metadata[0].Title);

                var parentPlexId = Guard.Against.Null(plexMeta.Metadata[0].ParentRatingKey);

                var parentRequest = new GetMediaMetaDataRequest
                {
                    RatingKey = parentPlexId
                };
                var parentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(parentRequest);

                if (parentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return new List<PlexMetadataBuilderItem>();

                var parentMetadata = Guard.Against.Null(parentMetadataResponse.Object?.MediaContainer);

                var parentFriendlyTitle = Guard.Against.Null(parentMetadata.Metadata[0].Title);

                var showPath = Guard.Against.Null(parentMetadata.Metadata[0].Location?[0].Path);

                var libraryInfo = await GetPlexItemLibraryInfoAsync(plexMeta, showPath);
                if (libraryInfo == null) return new List<PlexMetadataBuilderItem>();

                var seasonIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');

                friendlyTitle = $"{parentFriendlyTitle} - {friendlyTitle}";

                var item = new PlexMetadataBuilderItem
                {
                    PlexId = plexId,
                    FriendlyTitle = friendlyTitle,
                    DataType = MaintainerrDataType.Seasons,
                    LibraryName = libraryInfo.LibraryName,
                    LibraryId = (int)libraryInfo.LibrarySectionId,
                    MediaFileRelativePath = libraryInfo.CleanTvShowPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = $"Season{seasonIndex}"
                };

                if (asChild)
                {
                    item.IsChild = true;
                    item.ParentPlexId = parentId;
                }

                var items = new List<PlexMetadataBuilderItem> { item };

                if (!this._overlayBehaviorConfiguration.OverlaySeasonEpisodes)
                {
                    return items;
                }

                // Get Child Episodes
                logger.LogInformation("Including Metadata for Episodes for {PlexId} - {FriendlyTitle}", plexId,
                    friendlyTitle);
                var episodeInfos = await plexClient.GetMetadataChildrenAsync(plexId);

                var episodeIds = episodeInfos?.MediaContainer?.Metadata?.Where(x => x.RatingKey != null)
                    .Select(metadata =>
                        int.Parse(metadata.RatingKey!)).ToArray();

                if (episodeIds is null || episodeIds.Length <= 0)
                {
                    return items;
                }

                var childrenMaintainerrMedia = episodeIds
                    .Select(x => new MaintainerrMediaDto(x.ToString(), addDate))
                    .ToList();

                items.AddRange(await GatherEpisodeCollectionItems(collectionTitle, deleteAfterDays,
                    childrenMaintainerrMedia, true, plexId));

                return items;
            });
    }

    private async Task<IEnumerable<PlexMetadataBuilderItem>> GatherEpisodeCollectionItems(
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild = false,
        string? parentId = null)
    {
        return await GatherCollectionItems(
            MaintainerrDataType.Episodes,
            collectionTitle,
            deleteAfterDays,
            maintainerrMedia,
            asChild,
            async (plexMeta, plexId, _) =>
            {
                var friendlyTitle = Guard.Against.Null(plexMeta.Metadata[0].Title);

                var grandParentPlexId = Guard.Against.Null(plexMeta.Metadata[0].GrandparentRatingKey);

                var grandparentRequest = new GetMediaMetaDataRequest
                {
                    RatingKey = grandParentPlexId
                };
                var grandparentMetadataResponse = await plexApi.Library.GetMediaMetaDataAsync(grandparentRequest);

                if (grandparentMetadataResponse.StatusCode != (int)HttpStatusCode.OK)
                    return new List<PlexMetadataBuilderItem>();

                var grandparentMetadata = Guard.Against.Null(grandparentMetadataResponse.Object?.MediaContainer);

                var grandparentFriendlyTitle = Guard.Against.Null(grandparentMetadata.Metadata[0].Title);

                var showPath = Guard.Against.Null(grandparentMetadata.Metadata[0].Location?[0].Path);

                var libraryInfo = await GetPlexItemLibraryInfoAsync(plexMeta, showPath);
                if (libraryInfo == null) return new List<PlexMetadataBuilderItem>();

                var seasonIndex = plexMeta.Metadata[0].ParentIndex?.ToString().PadLeft(2, '0') ?? "00";
                var episodeIndex = plexMeta.Metadata[0].Index.ToString().PadLeft(2, '0');
                var episodeFileNameFormat = $"S{seasonIndex}E{episodeIndex}";

                var fullFriendlyTitle = $"{grandparentFriendlyTitle} - {episodeFileNameFormat} - {friendlyTitle}";

                var item = new PlexMetadataBuilderItem
                {
                    PlexId = plexId,
                    FriendlyTitle = fullFriendlyTitle,
                    DataType = MaintainerrDataType.Episodes,
                    LibraryName = libraryInfo.LibraryName,
                    LibraryId = (int)libraryInfo.LibrarySectionId,
                    MediaFileRelativePath = libraryInfo.CleanTvShowPath,
                    OriginalPlexPosterUrl = plexMeta.Metadata[0].Thumb,
                    MediaFileName = episodeFileNameFormat,
                };

                if (asChild)
                {
                    item.IsChild = true;
                    item.ParentPlexId = parentId;
                }

                return new List<PlexMetadataBuilderItem> { item };
            });
    }

    private async Task<IEnumerable<PlexMetadataBuilderItem>> GatherCollectionItems(
        MaintainerrDataType dataType,
        string? collectionTitle,
        int deleteAfterDays,
        List<MaintainerrMediaDto> maintainerrMedia,
        bool asChild,
        Func<GetMediaMetaDataMediaContainer, string, DateTime, Task<IEnumerable<PlexMetadataBuilderItem>>> buildItems)
    {
        if (asChild)
        {
            logger.LogDebug("Processing {DataType} collection as children", dataType);
        }

        var items = new List<PlexMetadataBuilderItem>();

        if (maintainerrMedia.Count == 0)
        {
            logger.LogInformation("No items found for collection: {Collection}", collectionTitle);
            return items;
        }

        foreach (var maintainerrItem in maintainerrMedia)
        {
            var plexId = maintainerrItem.PlexId;
            logger.LogDebug("Fetching Plex Metadata for {PlexId}", plexId);

            try
            {
                var request = new GetMediaMetaDataRequest
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
                var builtItems = await buildItems(plexMeta, maintainerrItem.PlexId, maintainerrItem.AddDate);

                foreach (var builtItem in builtItems)
                {
                    if (deleteAfterDays > 0)
                    {
                        var addDate = new DateTimeOffset(maintainerrItem.AddDate);
                        builtItem.HasExpiration = true;
                        builtItem.ExpirationDate = addDate.AddDays(deleteAfterDays);
                    }

                    builtItem.KometaLabelExists = await plexClient.HasKometaOverlay(plexId);

                    items.Add(builtItem);

                    if (!asChild)
                        logger.LogInformation(
                            "Grabbed Metadata for {ItemType} item with PlexId: {PlexId}, FriendlyTitle: {FriendlyTitle}, LibraryName: {LibraryName}, ItemRelativePath: {ItemRelativePath}, Exp Date: {ExpirationDate}",
                            builtItem.DataType,
                            builtItem.PlexId,
                            builtItem.FriendlyTitle,
                            builtItem.LibraryName,
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

    private async Task<PlexLibraryInfoDto?> GetPlexItemLibraryInfoAsync(GetMediaMetaDataMediaContainer plexMeta,
        string showPath)
    {

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
        var libraryPath = sectionPaths.FirstOrDefault(showPath.StartsWith);

        if (libraryPath is null)
            return null;

        var libraryName = Guard.Against.Null(plexMeta.LibrarySectionTitle,
            nameof(GetMediaMetaDataMediaContainer.LibrarySectionTitle));

        showPath = showPath.Replace(libraryPath, string.Empty);
        var cleanTvShowPath = Path.Join(libraryName, showPath);

        return new PlexLibraryInfoDto(librarySectionId, libraryPath, libraryName, cleanTvShowPath);
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

    private record MaintainerrMediaDto(string PlexId, DateTime AddDate);

    private record PlexLibraryInfoDto(
        long LibrarySectionId,
        string LibraryPath,
        string LibraryName,
        string CleanTvShowPath);

}
