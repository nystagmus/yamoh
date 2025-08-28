using LukeHagar.PlexAPI.SDK;
using Microsoft.Extensions.Logging;
using YAMOH.Infrastructure;

namespace YAMOH.Features.GetPlexInfo;

public class GetPlexInfoCommand(PlexAPI plexApi, ILogger<GetPlexInfoCommand> logger) : IYamohCommand
{
    public string CommandName => "get-plex-info";
    public string CommandDescription => "Test the PlexAPI and print the response";
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Testing PlexAPI");
        var response = await plexApi.Server.GetServerCapabilitiesAsync();

        if (response.StatusCode == (int)System.Net.HttpStatusCode.OK)
        {
            logger.LogInformation("Successfully retrieved server capabilities");
            logger.LogInformation("Server Info: {ServerInfo}", response.Object?.MediaContainer?.Version);
        }
    }
}
