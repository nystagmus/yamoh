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
using Serilog.Settings.Configuration;
using Serilog.Sinks.Spectre;
using Spectre.Console;
using Yamoh.Domain.State;
using Yamoh.Features.OverlayManager;
using Yamoh.Infrastructure;
using Yamoh.Infrastructure.Configuration;
using Yamoh.Infrastructure.Configuration.Validation;
using Yamoh.Infrastructure.EnvironmentUtility;
using Yamoh.Infrastructure.Extensions;
using Yamoh.Infrastructure.External;
using Yamoh.Infrastructure.FileProcessing;
using Yamoh.Infrastructure.ImageProcessing;
using Yamoh.Infrastructure.Scheduling;
using Yamoh.Ui;
using Log = Serilog.Log;

if (AppEnvironment.IsDocker)
{
    AnsiConsole.Console.Profile.Width = 80;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Spectre(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(AppEnvironment.LogFolder, "yamoh.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

Log.Information("Starting pre-build configuration..");

// Check and initialize config
var initializer = new AppFolderInitializer(AppEnvironment);
initializer.Initialize();

if (!initializer.CheckPermissions())
{
    Log.Information(
        "Access not permitted to configuration directory {AppEnvironmentConfigFolder}. Check your configuration",
        AppEnvironment.ConfigFolder);
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

// Rebuild serilog using configuration
Log.CloseAndFlush();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration, new ConfigurationReaderOptions { SectionName = "Logging" })
    .Enrich.FromLogContext()
    .WriteTo.Spectre()
    .WriteTo.File(Path.Combine(AppEnvironment.LogFolder, "yamoh.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Logging.AddSerilog();

builder.Services.AddOptions<YamohConfiguration>().Bind(builder.Configuration.GetSection(YamohConfiguration.Position))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<YamohConfiguration>, YamohConfigurationValidation>();

builder.Services.AddOptions<OverlayConfiguration>()
    .Bind(builder.Configuration.GetSection(OverlayConfiguration.Position)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<OverlayConfiguration>, OverlayConfigurationValidation>();

builder.Services.AddOptions<OverlayBehaviorConfiguration>()
    .Bind(builder.Configuration.GetSection(OverlayBehaviorConfiguration.Position)).ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ScheduleConfiguration>()
    .Bind(builder.Configuration.GetSection(ScheduleConfiguration.Position)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ScheduleConfiguration>, ScheduleConfigurationValidation>();

// Services
builder.Services.AddHttpClient();
builder.Services.AddTransient<CommandFactory>();
builder.Services.AddTransient<MaintainerrClient>();
builder.Services.AddTransient<PlexClient>();
builder.Services.AddTransient<OverlayHelper>();
builder.Services.AddTransient<AssetManager>();
builder.Services.AddTransient<PlexMetadataBuilder>();
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
var schedulerEnabled = builder.Configuration.GetSection(ScheduleConfiguration.Position).GetValue<bool>("Enabled");

if (schedulerEnabled)
{
    var overlayManagerCronSchedule = builder.Configuration.GetSection(ScheduleConfiguration.Position)
        .GetValue<string>("OverlayManagerCronSchedule");

    if (overlayManagerCronSchedule != null)
        builder.Services.AddCronJob<OverlayManagerJob>(overlayManagerCronSchedule);
    else throw new Exception("Schedule is enabled but OverlayManagerCronSchedule not found or could not be parsed.");
}

builder.Services.AddHostedService<CronScheduler>();

Log.Information("Starting up!");
var host = builder.Build();

ServiceLocator.SetServiceProvider(host.Services);

// Print configuration
SpectreConsoleHelper.PrintSplashScreen();

try
{
    var configs = new List<object>
    {
        host.Services.GetRequiredService<IOptions<YamohConfiguration>>().Value,
        host.Services.GetRequiredService<IOptions<OverlayConfiguration>>().Value,
        host.Services.GetRequiredService<IOptions<OverlayBehaviorConfiguration>>().Value,
        host.Services.GetRequiredService<IOptions<ScheduleConfiguration>>().Value
    };

    var configurationPanel = configs.PrintObjectPropertyValues();
    AnsiConsole.Write(configurationPanel);
}
catch (OptionsValidationException ex)
{
    foreach (var validationFailure in ex.Failures)
    {
        Log.Logger.Error(ex, "Configuration validation error: {ValidationFailure}", validationFailure);
    }

    return 0;
}
catch (Exception ex)
{
    Log.Logger.Error(ex, "Unhandled exception when reading from configuration");
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
    var scheduleOptions = scope.ServiceProvider.GetRequiredService<IOptions<ScheduleConfiguration>>().Value;

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
    public static AppEnvironment AppEnvironment { get; } = new();
}

public static class ServiceLocator
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }
}
