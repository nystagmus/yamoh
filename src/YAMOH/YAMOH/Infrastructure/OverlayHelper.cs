using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YAMOH.Infrastructure;

public class OverlayHelper(IOptions<YamohConfiguration> config, ILogger<OverlayHelper> logger)
{
    public FileInfo? AddOverlay(
        int plexId,
        string imagePath,
        string text,
        string? fontColor = null,
        string? backColor = null,
        string? fontPath = null,
        string? fontName = null,
        double? fontSize = null,
        int? padding = null,
        int? backRadius = null,
        int? horizontalOffset = null,
        string? horizontalAlign = null,
        int? verticalOffset = null,
        string? verticalAlign = null,
        int? backWidth = null,
        int? backHeight = null,
        ushort? textTransparency = null,
        ushort? backTransparency = null
    )
    {
        // Get config defaults
        var cfg = config.Value;
        fontColor ??= cfg.FontColor;
        backColor ??= cfg.BackColor;
        fontPath ??= cfg.FontPath;
        fontName ??= cfg.FontName;
        fontSize ??= cfg.FontSize;
        padding ??= cfg.Padding;
        backRadius ??= cfg.BackRadius;
        horizontalOffset ??= cfg.HorizontalOffset;
        horizontalAlign ??= cfg.HorizontalAlign;
        verticalOffset ??= cfg.VerticalOffset;
        verticalAlign ??= cfg.VerticalAlign;
        backWidth ??= cfg.BackWidth;
        backHeight ??= cfg.BackHeight;
        textTransparency ??= cfg.FontTransparency;
        backTransparency ??= cfg.BackTransparency;

        // Load image

        try
        {
            using var image = new MagickImage(imagePath);

            // Load font

            var fontFile = Path.Combine(fontPath, $"{fontName}.ttf");

            if (!File.Exists(fontFile))
            {
                fontFile = Path.Combine(Directory.GetCurrentDirectory(), "Fonts", "AvenirNextLTPro-Bold.ttf");
            }

            // calculate scaling
            var imageWidth = image.Width;
            var imageHeight = image.Height;
            var scaleFactor = imageWidth / 1000f;
            var scaledFontSize = (int)(fontSize.Value * scaleFactor);
            var scaledPadding = (int)(padding.Value * scaleFactor);
            var scaledBackRadius = (int)(backRadius.Value * scaleFactor);
            var scaledHorizontalOffset = (int)(horizontalOffset.Value * scaleFactor);
            var scaledVerticalOffset = (int)(verticalOffset.Value * scaleFactor);

            // todo: figure out the best way to handle "percentage" transparency
            // todo: also we're not handling it correctly if width and height are set in the options
            var backMagickColor = new MagickColor(backColor) { A = 45874 }; //{ A = backTransparency.Value };
            var fontMagickColor = new MagickColor(fontColor) { A = 45874 }; //{ A = textTransparency.Value };

            // Measure text
            var metrics = new Drawables()
                .FontPointSize(scaledFontSize)
                .Font(fontFile)
                .FillColor(fontMagickColor)
                .TextAlignment(TextAlignment.Center)
                .Text(0, 0, text).FontTypeMetrics(text);

            if (metrics == null)
            {
                logger.LogInformation("Could not determine properties of overlay to write. Check configuration");
                return null;
            }

            var scaledBackWidth = backWidth > 0
                ? (int)(backWidth.Value * scaleFactor)
                : (int)(metrics.TextWidth + scaledPadding * 2);
            var scaledPaddingY = scaledPadding + Math.Abs(metrics.Descent);

            var scaledBackHeight = backHeight > 0
                ? (int)(backHeight.Value * scaleFactor)
                : (int)(metrics.Ascent + scaledPaddingY * 2);

            // Alignment
            var x = horizontalAlign switch
            {
                "right" => imageWidth - scaledBackWidth - scaledHorizontalOffset,
                "center" => (imageWidth - scaledBackWidth) / 2,
                "left" => scaledHorizontalOffset,
                _ => imageWidth - scaledBackWidth - scaledHorizontalOffset
            };

            var y = verticalAlign switch
            {
                "bottom" => imageHeight - scaledBackHeight - scaledVerticalOffset,
                "center" => (imageHeight - scaledBackHeight) / 2,
                "top" => scaledVerticalOffset,
                _ => imageHeight - scaledBackHeight - scaledVerticalOffset
            };

            var textX = x + (scaledBackWidth / 2d) + scaledHorizontalOffset;
            var textY = y + scaledBackHeight - scaledPaddingY;

            var drawables = new Drawables()
                // Draw rounded rectangle background
                .FillColor(backMagickColor)
                .StrokeColor(backMagickColor)
                .RoundRectangle(x, y, x + scaledBackWidth, y + scaledBackHeight, scaledBackRadius, scaledBackRadius)
                // Draw Text
                .FontPointSize(scaledFontSize)
                .Font(fontFile)
                .FillColor(fontMagickColor)
                .TextAlignment(TextAlignment.Center)
                .Text(textX, textY, text);

            drawables.Draw(image);

            // // Add a visible debug marker (red circle at top-left corner)
            // var debugDrawables = new Drawables()
            //     .FillColor(MagickColors.Red)
            //     .StrokeColor(MagickColors.Red)
            //     .Ellipse(x, y, 20, 20, 0, 360)
            //     .FillColor(MagickColors.Blue)
            //     .StrokeColor(MagickColors.Blue)
            //     .Ellipse(textX, textY, 20, 20, 0, 360);
            //
            //
            // debugDrawables.Draw(image);

            var imagePathInfo = new FileInfo(imagePath);
            var tempPath = config.Value.TempImagePath;
            var tempImagePath = Path.Combine(tempPath, $"{plexId}_temp.{imagePathInfo.Extension}");
            image.Write(tempImagePath);

            return new FileInfo(tempImagePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Encountered an error while trying to read or write image file for {PlexId}", plexId);
            return null;
        }
    }
}
