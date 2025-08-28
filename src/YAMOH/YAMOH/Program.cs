using System.CommandLine;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using LukeHagar.PlexAPI.SDK;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using YAMOH;
using YAMOH.Domain.State;
using YAMOH.Features.OverlayManager;
using YAMOH.Infrastructure;
using YAMOH.Infrastructure.Configuration;
using YAMOH.Infrastructure.EnvironmentUtility;
using YAMOH.Infrastructure.Extensions;
using YAMOH.Infrastructure.External;
using YAMOH.Infrastructure.ImageProcessing;
using YAMOH.Infrastructure.Scheduling;
using YAMOH.Ui;
using Log = Serilog.Log;

if (AppEnvironment.IsDocker)
{
    AnsiConsole.Console.Profile.Width = 80;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Spectre(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(AppEnvironment.LogFolder, "yamoh.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

Log.Information("Starting pre-build configuration..");

// Check and initialize config
var initializer = new AppFolderInitializer(AppEnvironment);
initializer.Initialize();

if (!initializer.CheckPermissions())
{
    Log.Information("Access not permitted to configuration directory {AppEnvironmentConfigFolder}. Check your configuration", AppEnvironment.ConfigFolder);
    Environment.Exit(1);
}
initializer.CopyDefaultsIfMissing();

var builder = Host.CreateApplicationBuilder(args);

// Configuration
var appSettingsPath = Path.Combine(AppEnvironment.ConfigFolder, "appsettings.json");
builder.Configuration.AddJsonFile(appSettingsPath, optional: false, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

builder.Services.Configure<YamohConfiguration>(builder.Configuration.GetSection(YamohConfiguration.Position));
builder.Services.Configure<ScheduleOptions>(builder.Configuration.GetSection(ScheduleOptions.Position));

// Services
builder.Services.AddHttpClient();
builder.Services.AddTransient<CommandFactory>();
builder.Services.AddTransient<MaintainerrClient>();
builder.Services.AddTransient<PlexClient>();
builder.Services.AddTransient<OverlayHelper>();
builder.Services.AddSingleton<OverlayStateManager>();
builder.Services.AddAllTypesOf<IYamohCommand>(Assembly.GetExecutingAssembly());

builder.Services.AddTransient<PlexAPI>(provider =>
{
    var options = provider.GetService<IOptions<YamohConfiguration>>();

    if (options == null)
    {
        throw new Exception("Yamoh configuration not found");
    }

    var api = new PlexAPI(serverUrl: options.Value.PlexUrl, accessToken: options.Value.PlexToken);
    api.SDKConfiguration.Hooks.RegisterBeforeRequestHook(new PlexApiBeforeRequestHook());
    return api;
});

// Scheduler
var schedulerEnabled = builder.Configuration.GetSection(ScheduleOptions.Position).GetValue<bool>("Enabled");

if (schedulerEnabled)
{
    var overlayManagerCronSchedule = builder.Configuration.GetSection(ScheduleOptions.Position)
        .GetValue<string>("OverlayManagerCronSchedule");

    if (overlayManagerCronSchedule != null)
        builder.Services.AddCronJob<OverlayManagerJob>(overlayManagerCronSchedule);
    else throw new Exception("Schedule is enabled but OverlayManagerCronSchedule not found or could not be parsed.");
}

builder.Services.AddHostedService<CronScheduler>();

Log.Information("Looking good, starting up!");
var host = builder.Build();

ServiceLocator.SetServiceProvider(host.Services);

// Validate and print configuration
SpectreConsoleHelper.PrintSplashScreen();

var config = host.Services.GetRequiredService<IOptions<YamohConfiguration>>().Value;

try
{
    if (!config.AssertIsValid())
    {
        Environment.Exit(1);
    }
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

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    using var scope = host.Services.CreateScope();
    var scheduleOptions = scope.ServiceProvider.GetRequiredService<IOptions<ScheduleOptions>>().Value;

    if (scheduleOptions.Enabled)
    {
        AnsiConsole.MarkupLine("[green]Schedule is enabled.[/]");
        await host.RunAsync(cancellationToken);
        return;
    }

    var commands = scope.ServiceProvider.GetServices<IYamohCommand>().ToList();
    var commandNames = commands.Select(c => c.CommandName).ToList();

    // Prompt user
    var selectedCommandName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[cyan]Select a command to run:[/]")
            .AddChoices(commandNames));

    var selectedCommand =
        commands.FirstOrDefault(c => c.CommandName.Equals(selectedCommandName, StringComparison.OrdinalIgnoreCase));

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

public partial class Program
{
    public static AppEnvironment AppEnvironment {get;} = new();
}

public static class ServiceLocator
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }
}
