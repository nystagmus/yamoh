using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using Yamoh.Domain.Maintainerr;
using Yamoh.Infrastructure.Configuration;

namespace Yamoh.Infrastructure.External;

public class MaintainerrClient(
    IOptions<YamohConfiguration> config,
    IHttpClientFactory clientFactory,
    ILogger<MaintainerrClient> logger)
{
    private readonly YamohConfiguration _config = config.Value;
    private readonly HttpClient _httpClient = clientFactory.CreateClient("YAMOH");
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private Version? _cachedVersion;

    public async Task<Version?> GetVersionAsync()
    {
        if (_cachedVersion is not null) return _cachedVersion;

        try
        {
            var url = this._config.MaintainerrUrl.TrimEnd('/') + "/api/settings/version";

            var result = await this._httpClient.GetAsync(url);

            result.EnsureSuccessStatusCode();

            var versionString = await result.Content.ReadAsStringAsync();

            _cachedVersion = new Version(versionString);

            return _cachedVersion;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception encountered fetching Maintainerr Api Version");
            return null;
        }
    }

    public async Task<List<IMaintainerrCollectionResponse>> GetCollectionsAsync()
    {
        try
        {
            var url = this._config.MaintainerrUrl.TrimEnd('/') + "/api/collections";
            var version = await GetVersionAsync();

            return version?.Major >= 3
                ? await GetMaintainerrCollectionResponseListAsync<MaintainerrCollectionResponseV3>(url)
                : await GetMaintainerrCollectionResponseListAsync<MaintainerrCollectionResponseV2>(url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception encountered fetching MaintainerrCollections");
            return [];
        }
    }

    private async Task<List<IMaintainerrCollectionResponse>> GetMaintainerrCollectionResponseListAsync<T>(string url)
        where T : IMaintainerrCollectionResponse
    {
        var result = await this._httpClient.GetFromJsonAsync<List<T>>(url, _jsonOptions);
        return result?.Cast<IMaintainerrCollectionResponse>().ToList() ?? [];
    }

    public async Task<bool> ExecuteRules()
    {
        try
        {
            var url = this._config.MaintainerrUrl.TrimEnd('/') + "/api/rules/execute";

            var result = await this._httpClient.PostAsync(url, new StringContent(""));
            return result.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception encountered exceuting Maintainerr Rules");
            return false;
        }
    }
}
