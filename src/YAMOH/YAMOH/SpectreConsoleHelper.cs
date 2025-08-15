using Spectre.Console;
using System;
using Spectre.Console.Rendering;

namespace YAMOH;

public static class SpectreConsoleHelper
{
    public const int FixedWidth = 80;

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

    public static Panel CreatePanel(IRenderable content)
    {
        var panel = new Panel(content)
        {
            Padding = new Padding(1, 1, 1, 1),
            Border = BoxBorder.Rounded,
            Width = FixedWidth,
        };
        return panel;
    }

    public static Panel CreatePanel(string content)
    {
        var panel = new Panel(content)
        {
            Padding = new Padding(1, 1, 1, 1),
            Border = BoxBorder.Rounded,
            Width = FixedWidth,
        };
        return panel;
    }

    public static Table CreateTable()
    {
        var table = new Table
        {
            Border = TableBorder.Rounded
        };
        return table;
    }
}