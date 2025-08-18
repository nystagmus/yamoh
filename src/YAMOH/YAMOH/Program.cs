using System.CommandLine;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using LukeHagar.PlexAPI.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

// Services
builder.Services.AddHttpClient();
builder.Services.AddTransient<CommandFactory>();
builder.Services.AddTransient<MaintainerrClient>();
builder.Services.AddAllTypesOf<IYamohCommand>(Assembly.GetExecutingAssembly());

builder.Services.AddTransient<PlexAPI>(provider =>
{
    var options = provider.GetService<IOptions<YamohConfiguration>>();

    if (options == null)
    {
        throw new Exception("Yamoh configuration not found");
    }

    var api = new PlexAPI(serverUrl: options.Value.PlexUrl, accessToken:options.Value.PlexToken);
    api.SDKConfiguration.Hooks.RegisterBeforeRequestHook(new PlexApiBeforeRequestHook());
    return api;
});

var host = builder.Build();

ServiceLocator.SetServiceProvider(host.Services);

// Validate and print configuration
SpectreConsoleHelper.PrintSplashScreen();

var config = host.Services.GetRequiredService<IOptions<YamohConfiguration>>().Value;
try
{
    config.AssertIsValid();
    config.PrintConfigTable();
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Configuration error: {ex.Message}[/]");
    return 0;
}

var rootCommand = new RootCommand("Yet Another Maintainerr Overlay Helper");

var cliBuilder = host.Services.GetRequiredService<CommandFactory>();

foreach (var command in cliBuilder.GenerateCommandTree())
{
    rootCommand.Subcommands.Add(command);
}

rootCommand.SetAction(async (_, cancellationToken) =>
{
    using var scope = host.Services.CreateScope();
    var commands = scope.ServiceProvider.GetServices<IYamohCommand>().ToList();
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
        await selectedCommand.RunAsync(cancellationToken: cancellationToken);
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

public static class ServiceLocator
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }
}
