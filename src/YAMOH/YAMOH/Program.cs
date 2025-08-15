using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Vertical.SpectreLogger;
using YAMOH;

var builder = Host.CreateApplicationBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddSpectreConsole();

builder.Logging.AddSerilog(new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("prepnsb10.log", rollingInterval: RollingInterval.Day)
    .CreateLogger());

builder.Services.Configure<YamohConfiguration>(builder.Configuration.GetSection(YamohConfiguration.Position));

var host = builder.Build();

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
    return;
}

var rootCommand = new RootCommand("Yet Another Maintainerr Overlay Helper");

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();

await host.StopAsync();
