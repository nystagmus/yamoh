using Microsoft.Extensions.Options;
using YAMOH.Infrastructure.Configuration;

namespace YAMOH.Infrastructure.ImageProcessing;

public class AddOverlaySettings
{
    public required string FontColor { get; init; }
    public required string BackColor { get; init; }
    public required string FontPath { get; init; }
    public required string FontName { get; init; }
    public double FontSize { get; init; }
    public int Padding { get; init; }
    public int BackRadius { get; init; }
    public int HorizontalOffset { get; init; }
    public required string HorizontalAlign { get; init; }
    public int VerticalOffset { get; init; }
    public required string VerticalAlign { get; init; }
    public uint BackWidth { get; init; }
    public uint BackHeight { get; init; }
    public double FontTransparency { get; init; }
    public double BackTransparency { get; init; }

    public static AddOverlaySettings FromConfig(IOptions<YamohConfiguration> config)
    {
        var cfg = config.Value;
        var result = new AddOverlaySettings
        {
            FontColor = cfg.FontColor,
            BackColor = cfg.BackColor,
            FontPath = cfg.FontFullPath,
            FontName = cfg.FontName,
            FontSize = cfg.FontSize,
            Padding = cfg.Padding,
            BackRadius = cfg.BackRadius,
            HorizontalOffset = cfg.HorizontalOffset,
            HorizontalAlign = cfg.HorizontalAlign,
            VerticalOffset = cfg.VerticalOffset,
            VerticalAlign = cfg.VerticalAlign,
            BackWidth = cfg.BackWidth,
            BackHeight = cfg.BackHeight,
            FontTransparency = cfg.FontTransparency,
            BackTransparency = cfg.BackTransparency
        };
        return result;
    }
}
