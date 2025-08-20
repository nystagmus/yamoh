using LukeHagar.PlexAPI.SDK;
using Microsoft.Extensions.Logging;

namespace YAMOH.Commands;

public class GetPlexInfo(PlexAPI plexApi, ILogger<GetPlexInfo> logger) : IYamohCommand
{
    public string CommandName => "get-plex-info";
    public string CommandDescription => "Test the PlexAPI and print the response";
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Testing PlexAPI");
        var response = await plexApi.Server.GetServerCapabilitiesAsync();

        // Plex shit is not working. May have to do my own client
        if (response.StatusCode == (int)System.Net.HttpStatusCode.OK)
        {
            logger.LogInformation("Successfully retrieved server capabilities");
            logger.LogInformation("Server Info: {ServerInfo}", response.Object?.MediaContainer?.Version);
        }
    }
}
