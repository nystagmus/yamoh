using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Serilog;
using Yamoh.Infrastructure.Extensions;
using Yamoh.Infrastructure.ImageProcessing;

namespace Yamoh.Infrastructure.Configuration;

public class OverlayConfiguration
{
    public static string Position => "Overlay";

    public string FontName { get; init; } = "AvenirNextLTPro-Bold";
    public string FontColor { get; init; } = "#FFFFFF";

    [Range(0.0, 1.0, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public double FontTransparency { get; init; } = 1.00;

    public string BackColor { get; init; } = "#B20710";

    [Range(0.0, 1.0, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public double BackTransparency { get; init; } = 1.00;

    [Range(0.0, 500d, ErrorMessage = "Value for {0} must be greater than {1}.")]
    public double FontSize { get; init; } = 65d;

    public int Padding { get; init; } = 15;
    public int BackRadius { get; init; } = 20;
    public int HorizontalOffset { get; init; }
    public HorizontalAlignment HorizontalAlign { get; init; } = HorizontalAlignment.Center;
    public int VerticalOffset { get; init; }
    public VerticalAlignment VerticalAlign { get; init; } = VerticalAlignment.Bottom;
    public uint BackWidth { get; init; } = 1920;
    public uint BackHeight { get; init; } = 100;
    public string DateFormat { get; init; } = "MMM d";
    public string OverlayText { get; init; } = "Leaving";
    public bool EnableDaySuffix { get; init; } = true;
    public bool EnableUppercase { get; init; } = true;
    public string Language { get; init; } = "en-US";

    public string GetOverlayText(DateTimeOffset expirationDate)
    {
        var culture = new CultureInfo(Language);
        var formattedDate = expirationDate.ToString(DateFormat, culture);
        var overlayText = $"{OverlayText} {formattedDate}";
        if (EnableDaySuffix) overlayText += expirationDate.GetDaySuffix();
        if (EnableUppercase) overlayText = overlayText.ToUpper();
        return overlayText;
    }
}
