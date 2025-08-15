using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using YAMOH.Clients;

namespace YAMOH.Commands;

public class GetMaintainerrCollectionsCommand(
    MaintainerrClient maintainerrClient,
    ILogger<GetMaintainerrCollectionsCommand> logger) : IYamohCommandBase
{
    // public string CommandName => "get-maintainerr-collections";
    // public string CommandDescription => "Fetch the Maintainerr collections and print the info on the command line.";
    private const string Name = "get-maintainerr-collections";
    private const string Description = "Fetch the Maintainerr collections and print the info on the command line.";
    public string CommandName => Name;
    public string CommandDescription => Description;

    public static Command CreateCommand()
    {
        var command = new Command(Name, Description);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var maintainerr = ServiceLocator.ServiceProvider.GetRequiredService<MaintainerrClient>();
            var logger = ServiceLocator.ServiceProvider.GetRequiredService<ILogger<GetMaintainerrCollectionsCommand>>();

            var maintainerrCollectionsCommand = new GetMaintainerrCollectionsCommand(maintainerr, logger);
            await maintainerrCollectionsCommand.Run(cancellationToken);
        });
        return command;
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        try
        {
            var collections = await maintainerrClient.GetCollections();

            if (collections.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No collections found.[/]");
                return;
            }

            var table = SpectreConsoleHelper.CreateTable();
            table.Border = TableBorder.Rounded;
            table.AddColumn("Title");
            table.AddColumn("Description");
            table.AddColumn("Media Count");
            table.AddColumn("Is Active");
            table.AddColumn("Delete After Days");
            table.AddColumn("Type");

            foreach (var col in collections)
            {
                table.AddRow(
                    col.Title ?? "<none>",
                    string.IsNullOrWhiteSpace(col.Description) ? "<none>" : col.Description,
                    (col.Media?.Count ?? 0).ToString(),
                    col.IsActive ? "Yes" : "No",
                    col.DeleteAfterDays.ToString(),
                    col.Type.ToString()
                );
            }

            AnsiConsole.Write(table);

            foreach (var col in collections)
            {
                if (col.Media is not { Count: > 0 })
                {
                    continue;
                }

                var mediaTable = new Table
                {
                    Border = TableBorder.Rounded,
                };
                mediaTable.Title($"Media for Collection: [cyan]{col.Title}[/]");
                mediaTable.AddColumn("PlexId");
                mediaTable.AddColumn("TmdbId");
                mediaTable.AddColumn("Add Date");
                mediaTable.AddColumn("Image Path");
                mediaTable.AddColumn("Manual?");
                mediaTable.Expand();

                foreach (var media in col.Media)
                {
                    mediaTable.AddRow(
                        media.PlexId.ToString(),
                        media.TmdbId.ToString(),
                        media.AddDate.ToString("yyyy-MM-dd"),
                        media.ImagePath ?? "<none>",
                        media.IsManual ? "Yes" : "No"
                    );
                }

                AnsiConsole.Write(mediaTable);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching Maintainerr collections");
            AnsiConsole.MarkupLine($"[red]Error fetching Maintainerr collections: {ex.Message}[/]");
        }
    }
}