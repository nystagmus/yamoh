using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Infrastructure;

namespace YAMOH.Clients;

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
        uint? backWidth = null,
        uint? backHeight = null,
        double? fontTransparency = null,
        double? backTransparency = null
    )
    {
        // Get config defaults
        var cfg = config.Value;
        fontColor ??= cfg.FontColor;
        backColor ??= cfg.BackColor;
        fontPath ??= cfg.FontFullPath;
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
        fontTransparency ??= cfg.FontTransparency;
        backTransparency ??= cfg.BackTransparency;

        // Load image

        try
        {
            using var image = new MagickImage(imagePath);

            // Load font

            var fontFile = Path.Combine(fontPath, $"{fontName}.ttf");

            if (!File.Exists(fontFile))
            {
                fontFile = Path.Combine(cfg.FontFullPath, "AvenirNextLTPro-Bold.ttf");
            }

            if (!File.Exists(fontFile))
            {
                throw new FileNotFoundException($"Could not locate suitable font file using {fontPath}/{fontName}.ttf");
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

            // transparency
            var backAlpha = (ushort)(backTransparency * ushort.MaxValue)!;
            var fontAlpha = (ushort)(fontTransparency * ushort.MaxValue)!;
            var backMagickColor = new MagickColor(backColor) { A = backAlpha }; //{ A = backTransparency.Value };
            var fontMagickColor = new MagickColor(fontColor) { A = fontAlpha }; //{ A = textTransparency.Value };

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
                ? (uint)(backWidth.Value * scaleFactor)
                : (uint)(metrics.TextWidth + scaledPadding * 2);
            if (scaledBackWidth > imageWidth) scaledBackWidth = imageWidth;

            // Adjust vertical padding for text glyphs below baseline
            var scaledPaddingY = scaledPadding + Math.Abs(metrics.Descent);

            var scaledBackHeight = backHeight > 0
                ? (uint)(backHeight.Value * scaleFactor)
                : (uint)(metrics.Ascent + scaledPaddingY * 2);
            if (scaledBackHeight > imageHeight) scaledBackHeight = imageHeight;

            // Alignment
            var x = horizontalAlign switch
            {
                "right" => imageWidth - scaledBackWidth - scaledHorizontalOffset,
                "center" => (imageWidth - scaledBackWidth) / 2 + scaledHorizontalOffset,
                "left" => scaledHorizontalOffset,
                _ => imageWidth - scaledBackWidth - scaledHorizontalOffset
            };

            var y = verticalAlign switch
            {
                "bottom" => imageHeight - scaledBackHeight - scaledVerticalOffset,
                "center" => (imageHeight - scaledBackHeight) / 2 + scaledVerticalOffset,
                "top" => scaledVerticalOffset,
                _ => imageHeight - scaledBackHeight - scaledVerticalOffset
            };

            // always be visible
            if (x <= 0) x = 0;
            if (y <= 0) y = 0;

            var textX = x + (scaledBackWidth / 2d);
            var textY = y + scaledBackHeight - scaledPaddingY;

            var drawables = new Drawables()
                // Draw rounded rectangle background
                .FillColor(backMagickColor)
                .StrokeColor(backMagickColor);

            // Handle zero radius
            if (scaledBackRadius > 0)
            {
                drawables.RoundRectangle(x, y, x + scaledBackWidth, y + scaledBackHeight, scaledBackRadius,
                    scaledBackRadius);
            }
            else
            {
                drawables.Rectangle(x, y, x + scaledBackWidth, y + scaledBackHeight);
            }

            drawables
                // Draw Text
                .FontPointSize(scaledFontSize)
                .Font(fontFile)
                .FillColor(fontMagickColor)
                .TextAlignment(TextAlignment.Center)
                .Text(textX, textY, text);

            drawables.Draw(image);

            // // Add a visible debug marker (red dot at rect origin, blue dot at text origin)
            // var debugDrawables = new Drawables()
            //     .FillColor(MagickColors.Red)
            //     .StrokeColor(MagickColors.Red)
            //     .Ellipse(x, y, 20, 20, 0, 360)
            //     .FillColor(MagickColors.Blue)
            //     .StrokeColor(MagickColors.Blue)
            //     .Ellipse(textX, textY, 20, 20, 0, 360);
            //
            // debugDrawables.Draw(image);

            var imagePathInfo = new FileInfo(imagePath);
            var tempPath = config.Value.TempImageFullPath;
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
