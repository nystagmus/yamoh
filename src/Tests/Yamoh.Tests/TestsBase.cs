using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Tests;

public abstract class TestsBase
{
    protected string AppDirectory => AppDomain.CurrentDomain.BaseDirectory;
    protected string FontPath => Path.Combine(AppDirectory, "Fonts");

    protected const string Arial = "arial";

    protected string ArialPath => Path.Combine(AppDirectory, $"{Arial}.ttf");


    protected AddOverlaySettings GetAddOverlaySettings(Action<AddOverlaySettings>? modifiedWith = null)
    {
        var settings = new AddOverlaySettings
        {
            FontColor = "#FFFFFF",
            BackColor = "#000000",
            FontPath = FontPath,
            FontName = Arial,
            HorizontalAlign = "center",
            VerticalAlign = "middle",
            FontSize = 20,
            Padding = 5,
            BackRadius = 10,
            HorizontalOffset = 0,
            VerticalOffset = 0,
            BackWidth = 100,
            BackHeight = 50,
            FontTransparency = 1.0,
            BackTransparency = 1.0
        };

        modifiedWith?.Invoke(settings);

        return settings;
    }
}
