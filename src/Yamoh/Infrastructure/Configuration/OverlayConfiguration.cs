using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Humanizer;
using Humanizer.Localisation;
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
    public OverlayTextMode OverlayTextMode { get; init; } = OverlayTextMode.Date;
    public string OverlayText { get; init; } = "Leaving";
    public bool EnableUppercase { get; init; } = true;
    public string Language { get; init; } = "en-US";
    public string DateFormat { get; init; } = "MMM d";
    public bool DateEnableDaySuffix { get; init; } = true;
    public TimeUnit DaysLeftMinUnit { get; init; } = TimeUnit.Day;
    public TimeUnit DaysLeftMaxUnit { get; init; } = TimeUnit.Week;
    public int DaysLeftPrecision { get; init; } = 1;

    public string GetOverlayText(DateTimeOffset expirationDate)
    {
        switch (OverlayTextMode)
        {
            case OverlayTextMode.DaysLeft:
            {
                var culture = new CultureInfo(Language);
                var now = DateTimeOffset.UtcNow;
                var daysLeft = expirationDate.Date - now.Date;

                var humanizedDaysLeft = daysLeft.Humanize(precision: DaysLeftPrecision,
                    maxUnit: DaysLeftMaxUnit,
                    minUnit: DaysLeftMinUnit,
                    culture: culture);

                var overlayText = string.Join(' ', OverlayText, humanizedDaysLeft);
                if (EnableUppercase) overlayText = overlayText.ToUpper();
                return overlayText;
            }
            case OverlayTextMode.Date:
            {
                var culture = new CultureInfo(Language);
                var formattedDate = expirationDate.ToString(DateFormat, culture);
                var overlayText = string.Join(' ', OverlayText, formattedDate);
                if (DateEnableDaySuffix) overlayText += expirationDate.GetDaySuffix();
                if (EnableUppercase) overlayText = overlayText.ToUpper();
                return overlayText;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(OverlayTextMode), OverlayTextMode, $"Unknown overlay text style: {OverlayTextMode}");
        }
    }
}