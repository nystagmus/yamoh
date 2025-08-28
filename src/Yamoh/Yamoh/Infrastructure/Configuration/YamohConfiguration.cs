using System.Net.Sockets;
using Serilog;
using Spectre.Console;
using YAMOH.Infrastructure.Extensions;
using YAMOH.Ui;

namespace YAMOH.Infrastructure.Configuration;

public class YamohConfiguration
{
    public const string Position = "Yamoh";

    public string PlexUrl { get; init; } = string.Empty;
    public string PlexToken { get; init; } = string.Empty;
    public string MaintainerrUrl { get; init; } = string.Empty;
    public bool UseAssetMode { get; init; } = true;
    public string TempImagePath { get; init; } = string.Empty;
    public string AssetBasePath { get; init; } = string.Empty;
    public string BackupImagePath { get; init; } = string.Empty;
    public string FontPath { get; init; } = "Fonts";

    public string FontFullPath =>
        Path.IsPathRooted(FontPath)
            ? FontPath
            : Path.Combine(Program.AppEnvironment.ConfigFolder, FontPath);

    public string AssetBaseFullPath =>
        Path.IsPathRooted(AssetBasePath)
            ? AssetBasePath
            : Path.Combine(Program.AppEnvironment.ConfigFolder, AssetBasePath);

    public string TempImageFullPath =>
        Path.IsPathRooted(TempImagePath)
            ? TempImagePath
            : Path.Combine(Program.AppEnvironment.ConfigFolder, TempImagePath);

    public string BackupImageFullPath =>
        Path.IsPathRooted(BackupImagePath)
            ? BackupImagePath
            : Path.Combine(Program.AppEnvironment.ConfigFolder, BackupImagePath);

    public string FontName { get; init; } = "AvenirNextLTPro-Bold";
    public string FontColor { get; init; } = "#ffffff";
    public double FontTransparency { get; init; } = 1.00;
    public string BackColor { get; init; } = "#B20710";
    public double BackTransparency { get; init; } = 1.00;
    public double FontSize { get; init; } = 65d;
    public int Padding { get; init; } = 15;
    public int BackRadius { get; init; } = 20;
    public int HorizontalOffset { get; init; }
    public string HorizontalAlign { get; init; } = "center";
    public int VerticalOffset { get; init; }
    public string VerticalAlign { get; init; } = "bottom";
    public uint BackWidth { get; init; } = 1920;
    public uint BackHeight { get; init; } = 100;
    public string DateFormat { get; init; } = "MMM d";
    public string OverlayText { get; init; } = "Leaving";
    public bool EnableDaySuffix { get; init; } = true;
    public bool EnableUppercase { get; init; } = true;
    public string Language { get; init; } = "en-US";
    public bool ReapplyOverlays { get; init; }
    public bool OverlayShowSeasons { get; init; }
    public bool OverlaySeasonEpisodes { get; init; }
    public bool RestoreOnly { get; init; }

    public bool AssertIsValid()
    {
        var errors = new Dictionary<string, string>();

        try
        {
            ValidateUrl(PlexUrl, nameof(PlexUrl), errors);
            ValidateUrl(MaintainerrUrl, nameof(MaintainerrUrl), errors);

            if (string.IsNullOrWhiteSpace(PlexToken))
                errors.Add(nameof(PlexToken), "PlexToken must be provided.");

            if (string.IsNullOrWhiteSpace(FontPath))
                errors.Add(nameof(FontPath), "FontPath must be provided.");

            if (string.IsNullOrWhiteSpace(AssetBasePath))
                errors.Add(nameof(AssetBasePath), "AssetBasePath must be provided.");

            if (string.IsNullOrWhiteSpace(TempImagePath))
                errors.Add(nameof(TempImagePath), "TempImagePath must be provided.");

            ValidatePathFormat(AssetBasePath, nameof(AssetBasePath), errors);
            ValidatePathExists(AssetBaseFullPath, nameof(AssetBasePath), errors);
            ValidatePathIsWriteable(AssetBaseFullPath, nameof(AssetBasePath), errors);

            ValidatePathFormat(BackupImagePath, nameof(BackupImagePath), errors);
            ValidatePathExists(BackupImageFullPath, nameof(BackupImagePath), errors);
            ValidatePathIsWriteable(BackupImageFullPath, nameof(BackupImagePath), errors);

            ValidatePathFormat(TempImagePath, nameof(TempImagePath), errors);
            ValidateOrCreatePathExists(TempImageFullPath, nameof(TempImagePath), errors);
            ValidatePathIsWriteable(TempImageFullPath, nameof(TempImagePath), errors);

            ValidatePathFormat(FontPath, nameof(FontPath), errors);
            ValidatePathExists(FontFullPath, nameof(FontPath), errors);

            if (FontTransparency is < 0 or > 1)
                errors.Add(nameof(FontTransparency), "FontTransparency must be between 0.00 and 1.00.");

            if (BackTransparency is < 0 or > 1)
                errors.Add(nameof(BackTransparency), "BackTransparency must be between 0.00 and 1.00.");

            if (FontSize is < 1 or > 200)
                errors.Add(nameof(FontSize), "FontSize must be between 1 and 200.");

            if (Padding is < 0 or > 100)
                errors.Add(nameof(Padding), "Padding must be between 0 and 100.");

            if (BackRadius is < 0 or > 200)
                errors.Add(nameof(BackRadius), "BackRadius must be between 0 and 200.");

            if (HorizontalOffset is < -1000 or > 1000)
                errors.Add(nameof(HorizontalOffset), "HorizontalOffset must be between -1000 and 1000.");

            if (VerticalOffset is < -1000 or > 1000)
                errors.Add(nameof(VerticalOffset), "VerticalOffset must be between -1000 and 1000.");

            if (BackWidth > 10000)
                errors.Add(nameof(BackWidth), "BackWidth must be between 0 and 10000.");

            if (BackHeight > 10000)
                errors.Add(nameof(BackHeight), "BackHeight must be between 0 and 10000.");

            if (errors.Count <= 0)
            {
                return true;
            }

            var table = new Table().Title("[red]Configuration Validation Errors[/]");
            table.AddColumn("field");
            table.AddColumn("Issue");

            foreach (var error in errors)
                table.AddRow(error.Key, error.Value);

            table.Expand();
            AnsiConsole.Write(table);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception encountered when parsing configuration");
            return false;
        }
    }

    private static void ValidateUrl(string url, string propertyName, Dictionary<string, string> errors)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            errors.Add(propertyName, $"{propertyName} must be provided.");
            return;
        }

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            errors.Add(propertyName, $"{propertyName} is not a properly formatted Url.");
            return;
        }

        if (!IsUrlReachable(url))
        {
            errors.Add(propertyName, $"{propertyName} url: {url} is not reachable.");
        }
    }

    private static bool IsUrlReachable(string url)
    {
        try
        {
            var uri = new Uri(url);

            using var client = new TcpClient(uri.Host, uri.Port);

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ValidatePathExists(string path, string propertyName, Dictionary<string, string> errors)
    {
        if (errors.ContainsKey(propertyName)) return;

        if (!Directory.Exists(path))
        {
            errors.Add(propertyName, $"{propertyName} Path does not exist. Path: {path}");
        }
    }

    private static void ValidateOrCreatePathExists(string path, string propertyName, Dictionary<string, string> errors)
    {
        if (errors.ContainsKey(propertyName)) return;

        if (Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            errors.Add(propertyName, $"Could not create {propertyName} Path. Path: {path}");
        }
    }

    private static void ValidatePathIsWriteable(string path, string propertyName, Dictionary<string, string> errors)
    {
        if (errors.ContainsKey(propertyName)) return;

        if (!new DirectoryInfo(path).HasWritePermissions())
        {
            errors.Add(propertyName, $"{propertyName} is not writeable. Path: {path}");
        }
    }

    private static void ValidatePathFormat(string path, string propertyName, Dictionary<string, string> errors)
    {
        if (errors.ContainsKey(propertyName)) return;

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            errors.Add(propertyName, $"{propertyName} contains invalid path characters.");
    }

    public void PrintConfigTable()
    {
        var table = SpectreConsoleHelper.CreateTable();
        table.AddColumn("Property");
        table.AddColumn("Value");

        var props = this.GetType()
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        foreach (var prop in props)
        {
            var value = prop.GetValue(this)?.ToString() ?? "<null>";
            table.AddRow(prop.Name, value);
        }

        var panel = SpectreConsoleHelper.CreatePanel(table.Expand());

        panel.Header = new PanelHeader("YAMOH Configuration Used", Justify.Center);
        AnsiConsole.Write(panel);
    }
}
