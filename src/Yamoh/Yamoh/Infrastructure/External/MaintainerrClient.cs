using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Domain.Maintainerr;
using YAMOH.Infrastructure.Configuration;

namespace YAMOH.Infrastructure.External;

public class MaintainerrClient(
    IOptions<YamohConfiguration> config,
    IHttpClientFactory clientFactory,
    ILogger<MaintainerrClient> logger)
{
    private readonly YamohConfiguration _config = config.Value;
    private readonly HttpClient _httpClient = clientFactory.CreateClient("YAMOH");

    public async Task<List<MaintainerrCollection>> GetCollections()
    {
        try
        {
            var url = this._config.MaintainerrUrl.TrimEnd('/') + "/api/collections";

            var result =
                await this._httpClient.GetFromJsonAsync<List<MaintainerrCollection>>(url,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception encountered fetching MaintainerrCollections");
            return [];
        }
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
