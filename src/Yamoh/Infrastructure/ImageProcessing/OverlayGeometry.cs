using Ardalis.GuardClauses;
using ImageMagick;
using ImageMagick.Drawing;

namespace Yamoh.Infrastructure.ImageProcessing;

public class OverlayGeometry
{
    private readonly AddOverlaySettings _settings;
    private readonly ITypeMetric _typeMetric;
    private readonly uint _imageWidth;
    private readonly uint _imageHeight;
    private readonly float _scaleFactor;

    public OverlayGeometry(string text, MagickImage image, string fontFile, AddOverlaySettings settings)
    {
        this._imageWidth = image.Width;
        this._imageHeight = image.Height;
        this._settings = settings;

        this._scaleFactor = this._imageWidth / 1000f;

        var metric = new Drawables()
            .FontPointSize(ScaledFontSize)
            .Font(fontFile)
            .FillColor(new MagickColor("white"))
            .TextAlignment(TextAlignment.Center)
            .Text(0, 0, text).FontTypeMetrics(text);

        this._typeMetric = Guard.Against.Null(metric, nameof(metric),
            "Could not determine properties of overlay to write. Check configuration");
    }

    public IDrawables<ushort> GetDebugGlyphs()
    {
        var debugDrawables = new Drawables()
            .FillColor(MagickColors.Red)
            .StrokeColor(MagickColors.Red)
            .Ellipse(X, Y, 20, 20, 0, 360)
            .FillColor(MagickColors.Blue)
            .StrokeColor(MagickColors.Blue)
            .Ellipse(TextX, TextY, 20, 20, 0, 360);
        return debugDrawables;
    }

    public int ScaledFontSize => (int)(this._settings.FontSize * this._scaleFactor);
    public int ScaledPadding => (int)(this._settings.Padding * this._scaleFactor);
    public int ScaledBackRadius => (int)(this._settings.BackRadius * this._scaleFactor);
    public int ScaledHorizontalOffset => (int)(this._settings.HorizontalOffset * this._scaleFactor);
    public int ScaledVerticalOffset => (int)(this._settings.VerticalOffset * this._scaleFactor);

    public uint ScaledBackWidth
    {
        get
        {
            var scaledBackWidth = this._settings.BackWidth > 0
                ? (uint)(this._settings.BackWidth * this._scaleFactor)
                : (uint)(this._typeMetric.TextWidth + ScaledPadding * 2);
            if (scaledBackWidth > this._imageWidth) scaledBackWidth = this._imageWidth;
            return scaledBackWidth;
        }
    }

    public uint ScaledBackHeight
    {
        get
        {
            var scaledBackHeight = this._settings.BackHeight > 0
                ? (uint)(this._settings.BackHeight * this._scaleFactor)
                : (uint)(this._typeMetric.Ascent + ScaledPaddingY * 2);
            if (scaledBackHeight > this._imageHeight) scaledBackHeight = this._imageHeight;
            return scaledBackHeight;
        }
    }

    public double X
    {
        get
        {
            var x = this._settings.HorizontalAlign switch
            {
                HorizontalAlignment.Right => this._imageWidth - ScaledBackWidth - ScaledHorizontalOffset,
                HorizontalAlignment.Center => (this._imageWidth - ScaledBackWidth) / 2 + ScaledHorizontalOffset,
                HorizontalAlignment.Left => ScaledHorizontalOffset,
                _ => this._imageWidth - ScaledBackWidth - ScaledHorizontalOffset
            };
            // always be visible
            if (x <= 0) x = 0;
            return x;
        }
    }

    public double Y
    {
        get
        {
            var y = this._settings.VerticalAlign switch
            {
                VerticalAlignment.Bottom => this._imageHeight - ScaledBackHeight - ScaledVerticalOffset,
                VerticalAlignment.Center => (this._imageHeight - ScaledBackHeight) / 2 + ScaledVerticalOffset,
                VerticalAlignment.Top => ScaledVerticalOffset,
                _ => this._imageHeight - ScaledBackHeight - ScaledVerticalOffset
            };
            // always be visible
            if (y <= 0) y = 0;
            return y;
        }
    }

    public double TextX => X + (ScaledBackWidth / 2d);
    public double TextY => Y + ScaledBackHeight - ScaledPaddingY;

    public double ScaledPaddingY => ScaledPadding + Math.Abs(this._typeMetric.Descent);
}
