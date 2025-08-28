using System.Reflection;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace YAMOH.Ui;

public static class SpectreConsoleHelper
{
    public static void PrintSplashScreen()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var appName = assembly.GetName().Name;
        var version = assembly.GetName().Version;
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

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