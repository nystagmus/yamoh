using ImageMagick;
using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Tests;

public class OverlayGeometryTests : TestsBase
{
    [Fact]
    public void ScaledFontSize_ComputesCorrectly()
    {
        var settings = GetAddOverlaySettings(s => { s.FontSize = 20; });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);

        Assert.Equal(20, geometry.ScaledFontSize);
    }

    [Fact]
    public void X_AlignsRight()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.HorizontalAlign = "right";
            s.BackWidth = 100;
            s.HorizontalOffset = 10;
        });

        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);

        var expectedX = 1000 - geometry.ScaledBackWidth - geometry.ScaledHorizontalOffset;
        Assert.Equal(expectedX, geometry.X);
    }

    [Fact]
    public void Y_AlignsTop()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.VerticalAlign = "top";
            s.BackHeight = 50;
            s.VerticalOffset = 5;
        });

        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);

        Assert.Equal(geometry.ScaledVerticalOffset, geometry.Y);
    }

    [Fact]
    public void ScaledBackWidth_UsesCustomWidthAndCapsAtImageWidth()
    {
        var settings = GetAddOverlaySettings(s => { s.BackWidth = 2000; });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.Equal(1000u, geometry.ScaledBackWidth); // capped at image width
    }

    [Fact]
    public void ScaledBackWidth_CalculatesFromTextAndPadding()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.BackWidth = 0;
            s.Padding = 10;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.True(geometry.ScaledBackWidth > 0);
    }

    [Fact]
    public void ScaledBackHeight_UsesCustomHeightAndCapsAtImageHeight()
    {
        var settings = GetAddOverlaySettings(s => { s.BackHeight = 2000; });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.Equal(500u, geometry.ScaledBackHeight); // capped at image height
    }

    [Fact]
    public void ScaledBackHeight_CalculatesFromTypeMetricAndPaddingY()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.BackHeight = 0;
            s.Padding = 10;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.True(geometry.ScaledBackHeight > 0);
    }

    [Theory]
    [InlineData("left", 10)]
    [InlineData("center", null)] // will check for centering
    [InlineData("right", null)] // will check for right alignment
    public void X_AlignsCorrectly(string align, int? expectedLeft)
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.HorizontalAlign = align;
            s.BackWidth = 100;
            s.HorizontalOffset = 10;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);

        if (expectedLeft.HasValue)
            Assert.Equal(expectedLeft.Value, geometry.X);
        else if (align == "center")
            Assert.True(Math.Abs(geometry.X - ((1000 - geometry.ScaledBackWidth) / 2 + geometry.ScaledHorizontalOffset))
                        < 1);
        else if (align == "right")
            Assert.True(Math.Abs(geometry.X - (1000 - geometry.ScaledBackWidth - geometry.ScaledHorizontalOffset)) < 1);
    }

    [Theory]
    [InlineData("top", 5)]
    [InlineData("center", null)]
    [InlineData("bottom", null)]
    public void Y_AlignsCorrectly(string align, int? expectedTop)
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.VerticalAlign = align;
            s.BackHeight = 50;
            s.VerticalOffset = 5;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);

        if (expectedTop.HasValue)
            Assert.Equal(expectedTop.Value, geometry.Y);
        else if (align == "center")
            Assert.True(Math.Abs(geometry.Y - ((500 - geometry.ScaledBackHeight) / 2 + geometry.ScaledVerticalOffset))
                        < 1);
        else if (align == "bottom")
            Assert.True(Math.Abs(geometry.Y - (500 - geometry.ScaledBackHeight - geometry.ScaledVerticalOffset)) < 1);
    }

    [Fact]
    public void TextX_IsCenteredInBackWidth()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.HorizontalAlign = "center";
            s.BackWidth = 100;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.Equal(geometry.X + geometry.ScaledBackWidth / 2d, geometry.TextX);
    }

    [Fact]
    public void TextY_IsCorrectlyCalculated()
    {
        var settings = GetAddOverlaySettings(s =>
        {
            s.VerticalAlign = "bottom";
            s.BackHeight = 50;
            s.Padding = 10;
        });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.Equal(geometry.Y + geometry.ScaledBackHeight - geometry.ScaledPaddingY, geometry.TextY);
    }

    [Fact]
    public void ScaledPaddingY_HandlesNegativeDescent()
    {
        var settings = GetAddOverlaySettings(s => { s.Padding = 10; });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        Assert.True(geometry.ScaledPaddingY >= geometry.ScaledPadding);
    }

    [Fact]
    public void GetDebugGlyphs_ReturnsDrawables()
    {
        var settings = GetAddOverlaySettings(s => { });
        var image = new MagickImage(MagickColors.White, 1000, 500);
        var geometry = new OverlayGeometry("Test", image, ArialPath, settings);
        var glyphs = geometry.GetDebugGlyphs();
        Assert.NotNull(glyphs);
    }
}
