using System.ComponentModel.DataAnnotations;

namespace Yamoh.Infrastructure.Configuration;

public class YamohConfiguration
{
    public static string Position => "Yamoh";

    // App
    [Required(ErrorMessage = "{0} is required.")]
    public string PlexUrl { get; init; } = string.Empty;

    [Required(ErrorMessage = "{0} is required.")]
    public string PlexToken { get; init; } = string.Empty;

    [Required(ErrorMessage = "{0} is required.")]
    public string MaintainerrUrl { get; init; } = string.Empty;

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
}
