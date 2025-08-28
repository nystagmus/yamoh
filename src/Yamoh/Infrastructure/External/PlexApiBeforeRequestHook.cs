using System.Net.Http.Headers;
using LukeHagar.PlexAPI.SDK.Hooks;

namespace Yamoh.Infrastructure.External;

public class PlexApiBeforeRequestHook : IBeforeRequestHook
{
    public Task<HttpRequestMessage> BeforeRequestAsync(BeforeRequestContext hookCtx, HttpRequestMessage request)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return Task.FromResult(request);
    }
}
