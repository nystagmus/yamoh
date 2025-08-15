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

var rootCommand = new RootCommand("Yet Another Maintainerr Overlay Helper");

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();

await host.StopAsync();
