using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using LukeHagar.PlexAPI.SDK.Hooks;
using LukeHagar.PlexAPI.SDK.Models.Errors;
using LukeHagar.PlexAPI.SDK.Models.Requests;
using LukeHagar.PlexAPI.SDK.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Directory = System.IO.Directory;

namespace YAMOH.Clients;

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
        _config = config.Value;
        _httpClient = clientFactory.CreateClient("YAMOH");
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<FileInfo?> DownloadPlexImageAsync(string urlStub)
    {
        // Ensure temp directory exists
        var tempDirectory = this._config.TempImagePath;

        if (!Path.IsPathRooted(tempDirectory))
        {
            tempDirectory = Path.Combine(this._config.AssetBasePath, tempDirectory);
        }

        Directory.CreateDirectory(tempDirectory);

        // Build full URL
        var plexUrl = new Uri(_config.PlexUrl.TrimEnd('/'));
        var plexToken = _config.PlexToken;
        var urlStubWithAuth = $"{urlStub}?X-Plex-Token={plexToken}";
        var fullUrl = new Uri(plexUrl, urlStubWithAuth);

        try
        {
            var response = await _httpClient.GetAsync(fullUrl);
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
            _logger.LogError(ex, "Failed to download Plex image: {FullUrl}", fullUrl.AbsolutePath);
            return null;
        }
    }

    public async Task<GetMetadataChildrenResponseBody?> GetMetadataChildrenAsync(int ratingKey)
    {
        // Build full URL
        var plexUrl = new Uri(_config.PlexUrl.TrimEnd('/'));
        var plexToken = _config.PlexToken;
        var urlStubWithAuth = $"/library/metadata/{ratingKey}/children?X-Plex-Token={plexToken}";
        var fullUrl = new Uri(plexUrl, urlStubWithAuth);

        try
        {
            var result = await this._httpClient.GetFromJsonAsync<GetMetadataChildrenResponseBody>(fullUrl,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result;
        }
        catch (Exception ex)
        {
            // Log error (strip the params because the only param is our auth key
            _logger.LogError(ex, "Failed to download Plex Metadata Children: {FullUrl}", fullUrl.AbsolutePath);
            return null;
        }
    }
}
