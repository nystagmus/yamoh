using System.CommandLine;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Spectre.Console;
using Vertical.SpectreLogger;
using YAMOH;
using YAMOH.Clients;
using YAMOH.Commands;
using YAMOH.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSpectreConsole();

builder.Logging.AddSerilog(new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("yamoh.log", rollingInterval: RollingInterval.Day)
    .CreateLogger());

builder.Services.Configure<YamohConfiguration>(builder.Configuration.GetSection(YamohConfiguration.Position));

builder.Services.AddHttpClient();
builder.Services.AddTransient<MaintainerrClient>();
builder.Services.AddAllTypesOf<IYamohCommandBase>(Assembly.GetExecutingAssembly(), ServiceLifetime.Scoped);

var host = builder.Build();
ServiceLocator.SetServiceProvider(host.Services);
// Validate and print configuration
SpectreConsoleHelper.PrintSplashScreen();

var config = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<YAMOH.YamohConfiguration>>().Value;
try
{
    config.AssertIsValid();
    config.PrintConfigTable();
}
catch (Exception ex)
{
    Spectre.Console.AnsiConsole.MarkupLine($"[red]Configuration error: {ex.Message}[/]");
    return 0;
}

var rootCommand = new RootCommand("Yet Another Maintainerr Overlay Helper");

CreateMaintainerrCommand(rootCommand);

rootCommand.SetAction(async (_, cancellationToken) =>
{
    using var scope = host.Services.CreateScope();
    var commands = scope.ServiceProvider.GetServices<IYamohCommandBase>().ToList();
    var commandNames = commands.Select(c => c.CommandName).ToList();

    // Prompt user
    var selectedCommandName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[cyan]Select a command to run:[/]")
            .AddChoices(commandNames));

    var selectedCommand = commands.FirstOrDefault(c => c.CommandName.Equals(selectedCommandName, StringComparison.OrdinalIgnoreCase));

    if (selectedCommand == null)
    {
        AnsiConsole.MarkupLine($"[red]No command with the name {selectedCommandName} found.[/]");
        return;
    }

    try
    {
        AnsiConsole.MarkupLine($"[green]Running '{selectedCommand.CommandDescription}'[/]");
        await selectedCommand.Run(cancellationToken: cancellationToken);
    }
    catch (ValidationException vex)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]Validation failed:[/] {vex.Message}");
    }
    catch (TargetInvocationException tex) when (tex.InnerException is ValidationException vex)
    {
        AnsiConsole.MarkupLineInterpolated($"[red]Validation failed:[/] {vex.Message}");
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
    }
});

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();

void CreateMaintainerrCommand(Command parentCommand)
{
    var maintainerrSubCommand = new Command("maintainerr", "Commands to work with Maintainerr directly");

    maintainerrSubCommand.Subcommands.Add(GetMaintainerrCollectionsCommand.CreateCommand());

    parentCommand.Add(maintainerrSubCommand);
}

public static class ServiceLocator
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }
}
