using Spectre.Console;
using System;
using Spectre.Console.Rendering;

namespace YAMOH;

public static class SpectreConsoleHelper
{
    public static void PrintSplashScreen()
    {
        var appName = "YAMOH";
        var version = "v1.0.0"; // Optionally, retrieve from assembly
        var description = "Yet Another Maintainerr Overlay Helper";

        var figlet = new FigletText(appName)
            .Centered()
            .Color(Color.Cyan1);

        var panelContent = $"[bold yellow]{version}[/]\n[grey]{description}[/]";
        var panel = CreatePanel(panelContent);

        panel.Header = new PanelHeader("Welcome", Justify.Center);

        var layout = new Layout()
            .SplitRows(new Layout("Top"),
                new Layout("Bottom"));
        layout["Top"].Update(figlet);
        layout["Bottom"].Update(panel);

        AnsiConsole.Write(layout);
    }

    public static void PrintKometaAssetGuide()
    {
        var panelContent = @"[bold yellow]To make it work with Kometa:[/]
[cyan]1. Follow the directions on Kometa's website for setting up asset directories: https://metamanager.wiki/en/latest/kometa/guides/assets
2. You must disable caching[/][gray] `cache: false`[/]
[cyan]3. For each collection you must set operation's [/][gray] `mass_poster_update: true`[/]";
        var panel = CreatePanel(panelContent);
        panel.Header = new PanelHeader("Kometa Instructions", Justify.Center);
        AnsiConsole.Write(panel);
    }

    public static Panel CreatePanel(IRenderable content)
    {
        var panel = new Panel(content)
        {
            Padding = new Padding(1, 1, 1, 1),
            Border = BoxBorder.Rounded,
        };
        return panel.Expand();
    }

    public static Panel CreatePanel(string content)
    {
        var panel = new Panel(content)
        {
            Padding = new Padding(1, 1, 1, 1),
            Border = BoxBorder.Rounded,
        };
        return panel.Expand();
    }

    public static Table CreateTable()
    {
        var table = new Table
        {
            Border = TableBorder.Rounded
        };
        return table.Expand();
    }
}