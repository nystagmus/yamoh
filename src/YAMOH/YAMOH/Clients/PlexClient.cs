using System.Text.Encodings.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YAMOH.Clients;

public class PlexClient(
    IOptions<YamohConfiguration> config,
    IHttpClientFactory clientFactory,
    ILogger<MaintainerrClient> logger)
{
    private readonly YamohConfiguration _config = config.Value;
    private readonly HttpClient _httpClient = clientFactory.CreateClient("YAMOH");


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
            logger?.LogError(ex, "Failed to download Plex image: {FullUrl}", fullUrl.AbsolutePath);
            return null;
        }
    }

}
