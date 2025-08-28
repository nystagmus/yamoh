using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YAMOH.Infrastructure.Configuration;

namespace YAMOH.Infrastructure.ImageProcessing;

public class OverlayHelper(IOptions<YamohConfiguration> config, ILogger<OverlayHelper> logger)
{
    public FileInfo? AddOverlay(
        int plexId,
        string imagePath,
        string text,
        AddOverlaySettings settings
    )
    {
        try
        {
            var cfg = config.Value;

            // Load Image
            using var image = new MagickImage(imagePath);

            // Load font
            var fontFile = ValidateFontFile(settings, cfg);

            // get geometry
            var geometry = new OverlayGeometry(text, image, fontFile, settings);

            // transparency
            var backAlpha = (ushort)(settings.BackTransparency * ushort.MaxValue)!;
            var fontAlpha = (ushort)(settings.FontTransparency * ushort.MaxValue)!;

            var backMagickColor = new MagickColor(settings.BackColor)
                { A = backAlpha };

            var fontMagickColor = new MagickColor(settings.FontColor)
                { A = fontAlpha };

            var drawables = new Drawables()
                // Draw rounded rectangle background
                .FillColor(backMagickColor)
                .StrokeColor(backMagickColor);

            // Handle zero radius
            if (geometry.ScaledBackRadius > 0)
            {
                drawables.RoundRectangle(
                    geometry.X,
                    geometry.Y,
                    geometry.X + geometry.ScaledBackWidth,
                    geometry.Y + geometry.ScaledBackHeight,
                    geometry.ScaledBackRadius,
                    geometry.ScaledBackRadius);
            }
            else
            {
                drawables.Rectangle(
                    geometry.X,
                    geometry.Y,
                    geometry.X + geometry.ScaledBackWidth,
                    geometry.Y + geometry.ScaledBackHeight);
            }

            drawables
                // Draw Text
                .FontPointSize(geometry.ScaledFontSize)
                .Font(fontFile)
                .FillColor(fontMagickColor)
                .TextAlignment(TextAlignment.Center)
                .Text(geometry.TextX, geometry.TextY, text);

            drawables.Draw(image);

            // var debugDrawables = geometry.GetDebugGlyphs();
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

    private static string ValidateFontFile(AddOverlaySettings settings, YamohConfiguration cfg)
    {
        var fontFile = Path.Combine(settings.FontPath, $"{settings.FontName}.ttf");

        if (!File.Exists(fontFile))
        {
            fontFile = Path.Combine(cfg.FontFullPath, "AvenirNextLTPro-Bold.ttf");
        }

        if (!File.Exists(fontFile))
        {
            throw new FileNotFoundException(
                $"Could not locate suitable font file using {settings.FontPath}/{settings.FontName}.ttf");
        }

        return fontFile;
    }
}
