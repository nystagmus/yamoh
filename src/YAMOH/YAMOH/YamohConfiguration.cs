namespace YAMOH;

public class YamohConfiguration
{
    public const string Position = "Yamoh";

    public string PlexUrl { get; set; } = string.Empty;
    public string PlexToken { get; set; } = string.Empty;
    public string MaintainerrUrl { get; set; } = string.Empty;
    public string ImageSavePath { get; set; } = string.Empty;
    public string OriginalImagePath { get; set; } = string.Empty;
    public string TempImagePath { get; set; } = string.Empty;
    public bool UseAssetMode { get; set; } = true;
    public string AssetBasePath { get; set; } = string.Empty;
    public string FontPath { get; set; } = string.Empty;
    public string FontName { get; set; } = string.Empty;
    public string FontColor { get; set; } = "#ffffff";
    public int FontTransparency { get; set; } = 256;
    public string BackColor { get; set; } = "#B20710";
    public int BackTransparency { get; set; } = 256;
    public int FontSize { get; set; } = 65;
    public int Padding { get; set; } = 15;
    public int BackRadius { get; set; } = 20;
    public int HorizontalOffset { get; set; } = 0;
    public string HorizontalAlign { get; set; } = "center";
    public int VerticalOffset { get; set; } = 0;
    public string VerticalAlign { get; set; } = "bottom";
    public int BackWidth { get; set; } = 1920;
    public int BackHeight { get; set; } = 100;
    public string DateFormat { get; set; } = "MMM d";
    public string OverlayText { get; set; } = "Leaving";
    public int RunInterval { get; set; } = 480;
    public bool EnableDaySuffix {get;set;} = true;
    public bool EnableUppercase { get; set; } = true;
    public string Language { get; set; } = "en-US";
}
