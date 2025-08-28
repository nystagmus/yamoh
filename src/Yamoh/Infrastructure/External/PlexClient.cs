using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yamoh.Domain.Maintainerr;
using Yamoh.Domain.Plex;
using Yamoh.Infrastructure.Configuration;
using Directory = System.IO.Directory;

namespace Yamoh.Infrastructure.External;

public class PlexClient
{
    private readonly ILogger<PlexClient> _logger;
    private readonly YamohConfiguration _config;
    private readonly HttpClient _httpClient;

    public PlexClient(
        IOptions<YamohConfiguration> config,
        IHttpClientFactory clientFactory,
        ILogger<PlexClient> logger)
    {
        this._logger = logger;
        this._config = config.Value;
        this._httpClient = clientFactory.CreateClient("YAMOH");
        this._httpClient.DefaultRequestHeaders.Accept.Clear();
        this._httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // The SDK implementation was broken when this was originally developed
    private Uri BuildPlexUrl(string urlStub)
    {
        var plexUrl = new Uri(this._config.PlexUrl.TrimEnd('/'));
        var fullUrl = new Uri(plexUrl, urlStub);
        var uriBuilder = new UriBuilder(fullUrl);
        var queryParameters = HttpUtility.ParseQueryString(uriBuilder.Query);
        var plexToken = this._config.PlexToken;
        queryParameters["X-Plex-Token"] = plexToken;
        uriBuilder.Query = queryParameters.ToString();
        return uriBuilder.Uri;
    }

    private static string RemoveQueryParameter(string urlString, string parameterToRemove)
    {
        try
        {
            var uriBuilder = new UriBuilder(urlString);
            var queryParameters = HttpUtility.ParseQueryString(uriBuilder.Query);

            queryParameters.Remove(parameterToRemove);

            uriBuilder.Query = queryParameters.ToString();
            return uriBuilder.ToString();
        }
        catch (UriFormatException)
        {
            // Handle invalid URI format if necessary
            return urlString;
        }
    }

    private static string RemovePlexAuthToken(string urlString)
        => RemoveQueryParameter(urlString, "X-Plex-Token");

    private static string RemovePlexAuthToken(Uri url)
        => RemovePlexAuthToken(url.ToString());

    public async Task<FileInfo?> DownloadPlexImageAsync(string urlStub)
    {
        // Ensure temp directory exists
        var tempDirectory = this._config.TempImageFullPath;

        Directory.CreateDirectory(tempDirectory);

        // Build full URL
        var fullUrl = BuildPlexUrl(urlStub);

        try
        {
            var response = await this._httpClient.GetAsync(fullUrl);
            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

            var ext = contentType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/jpg" => ".jpg",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            // Generate filename
            var fileName = urlStub.Replace('/', '_') + ext;
            var filePath = Path.Combine(tempDirectory, fileName);

            await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);

            return new FileInfo(filePath);
        }
        catch (Exception ex)
        {
            // Log error (strip the params because the only param is our auth key
            this._logger.LogError(ex, "Failed to download Plex image: {FullUrl}", RemovePlexAuthToken(fullUrl));
            return null;
        }
    }

    // The SDK implementation was broken when this was originally developed
    public async Task<GetMetadataChildrenResponseBody?> GetMetadataChildrenAsync(int ratingKey)
    {
        // Build full URL
        var urlStub = $"/library/metadata/{ratingKey}/children";
        var fullUrl = BuildPlexUrl(urlStub);

        try
        {
            var result = await this._httpClient.GetFromJsonAsync<GetMetadataChildrenResponseBody>(fullUrl,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result;
        }
        catch (Exception ex)
        {
            // Log error (strip the params because the only param is our auth key
            this._logger.LogError(ex, "Failed to download Plex Metadata Children: {FullUrl}",
                RemovePlexAuthToken(fullUrl));
            return null;
        }
    }

    public async Task<List<PlexLabel>?> GetLabelsForPlexIdAsync(int plexId)
    {
        var urlStub = $"/library/metadata/{plexId}";
        var fullUrl = BuildPlexUrl(urlStub);

        try
        {
            var response = await this._httpClient.GetFromJsonAsync<PlexMetadataResponse>(fullUrl,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var labels = response?.MediaContainer?.Metadata?.FirstOrDefault()?.Label;
            return labels;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch labels for PlexId: {FullUrl}", RemovePlexAuthToken(fullUrl));
            return null;
        }
    }

    public async Task<bool> RemoveLabelKeyFromItem(long librarySectionId, int plexId, MaintainerrPlexDataType type,
        string labelTag)
    {
        // PUT http://{ip_address}:32400/library/sections/{library_id}/all?type=1&id={movie_id}&includeExternalMedia={include_external_media}&{parameter_values}&X-Plex-Token={plex_token}
        var plexDataType = (int)type;

        var urlStub =
            $"/library/sections/{librarySectionId}/all?type={plexDataType}&id={plexId}&includeExternalMedia=1&label[].tag.tag-={labelTag}";
        var fullUrl = BuildPlexUrl(urlStub);

        try
        {
            var response = await this._httpClient.PutAsync(fullUrl, new StringContent(string.Empty));
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch labels for PlexId: {FullUrl}", RemovePlexAuthToken(fullUrl));
            return false;
        }
    }
}
