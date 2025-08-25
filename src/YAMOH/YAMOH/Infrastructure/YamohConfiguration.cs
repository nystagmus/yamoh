using Ardalis.GuardClauses;
using Spectre.Console;

namespace YAMOH.Infrastructure;

public class YamohConfiguration
{
    public const string Position = "Yamoh";

    public string PlexUrl { get; set; } = string.Empty;
    public string PlexToken { get; set; } = string.Empty;
    public string MaintainerrUrl { get; set; } = string.Empty;
    public string TempImagePath { get; set; } = string.Empty;
    public bool UseAssetMode { get; set; } = true;
    public string AssetBasePath { get; set; } = string.Empty;
    public string BackupImagePath { get; set; } = string.Empty;
    public string FontPath { get; set; } = "Fonts";
    public string FontName { get; set; } = "AvenirNextLTPro-Bold";
    public string FontColor { get; set; } = "#ffffff";
    public double FontTransparency { get; set; } = 1.00;
    public string BackColor { get; set; } = "#B20710";
    public double BackTransparency { get; set; } = 1.00;
    public double FontSize { get; set; } = 65d;
    public int Padding { get; set; } = 15;
    public int BackRadius { get; set; } = 20;
    public int HorizontalOffset { get; set; } = 0;
    public string HorizontalAlign { get; set; } = "center";
    public int VerticalOffset { get; set; } = 0;
    public string VerticalAlign { get; set; } = "bottom";
    public uint BackWidth { get; set; } = 1920;
    public uint BackHeight { get; set; } = 100;
    public string DateFormat { get; set; } = "MMM d";
    public string OverlayText { get; set; } = "Leaving";
    public int RunInterval { get; set; } = 480;
    public bool EnableDaySuffix {get;set;} = true;
    public bool EnableUppercase { get; set; } = true;
    public string Language { get; set; } = "en-US";
    public bool ReapplyOverlays { get; set; }
    public bool OverlayShowSeasons { get; set; }
    public bool OverlaySeasonEpisodes { get; set; }
    public bool RestoreOnly { get; set; }

    public bool AssertIsValid()
    {
        Guard.Against.NullOrWhiteSpace(PlexUrl, nameof(PlexUrl), "PlexUrl must be provided.");
        Guard.Against.NullOrWhiteSpace(PlexToken, nameof(PlexToken), "PlexToken must be provided.");
        Guard.Against.NullOrWhiteSpace(MaintainerrUrl, nameof(MaintainerrUrl), "MaintainerrUrl must be provided.");
        Guard.Against.NullOrWhiteSpace(FontPath, nameof(FontPath), "FontPath must be provided.");
        Guard.Against.NullOrWhiteSpace(AssetBasePath, nameof(AssetBasePath), "AssetBasePath must be provided.");
        Guard.Against.NullOrWhiteSpace(TempImagePath, nameof(TempImagePath), "TempImagePath must be provided.");

        // Validate path formats (allow absolute or relative)
        ValidatePathFormat(AssetBasePath, nameof(AssetBasePath));
        ValidatePathFormat(TempImagePath, nameof(TempImagePath));
        ValidatePathFormat(BackupImagePath, nameof(BackupImagePath));
        ValidatePathFormat(FontPath, nameof(FontPath));

        // For FontPath, check that the file exists (using resolved absolute path)
        var fontFullPath = Path.IsPathRooted(FontPath)
            ? FontPath
            : Path.GetFullPath(FontPath);
        if (!Directory.Exists(fontFullPath))
            throw new FileNotFoundException($"FontPath path not found: {fontFullPath}");

        // Validate numeric values
        Guard.Against.OutOfRange(FontTransparency, nameof(FontTransparency), 0, 1);
        Guard.Against.OutOfRange(BackTransparency, nameof(BackTransparency), 0, 1);
        Guard.Against.OutOfRange(FontSize, nameof(FontSize), 1, 200);
        Guard.Against.OutOfRange(Padding, nameof(Padding), 0, 100);
        Guard.Against.OutOfRange(BackRadius, nameof(BackRadius), 0, 200);
        Guard.Against.OutOfRange(HorizontalOffset, nameof(HorizontalOffset), -1000, 1000);
        Guard.Against.OutOfRange(VerticalOffset, nameof(VerticalOffset), -1000, 1000);
        Guard.Against.OutOfRange(BackWidth, nameof(BackWidth), (uint)0, (uint)10000);
        Guard.Against.OutOfRange(BackHeight, nameof(BackHeight), (uint)0, (uint)10000);
        Guard.Against.OutOfRange(RunInterval, nameof(RunInterval), 1, 10000);

        return true;
    }

    private void ValidatePathFormat(string path, string propertyName)
    {
        // Accept both absolute and relative paths, but check for invalid path chars
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new ArgumentException($"{propertyName} contains invalid path characters.");
    }

    public void PrintConfigTable()
    {
        var table = SpectreConsoleHelper.CreateTable();
        table.AddColumn("Property");
        table.AddColumn("Value");

        var props = this.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var prop in props)
        {
            var value = prop.GetValue(this)?.ToString() ?? "<null>";
            table.AddRow(prop.Name, value);
        }

        var panel = SpectreConsoleHelper.CreatePanel(table.Expand());

        panel.Header = new PanelHeader("YAMOW Configuration Used", Justify.Center);
        AnsiConsole.Write(panel);
    }
}