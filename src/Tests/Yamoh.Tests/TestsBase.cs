using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Tests;

public abstract class TestsBase
{
    protected AddOverlaySettings GetAddOverlaySettings(Action<AddOverlaySettings>? modifiedWith = null)
    {
        var settings = new AddOverlaySettings
        {
            FontColor = "#FFFFFF",
            BackColor = "#000000",
            FontPath = "Arial.ttf",
            FontName = "Arial",
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
