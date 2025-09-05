using Yamoh.Infrastructure.Extensions;
using Yamoh.Infrastructure.External;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetManager(PlexClient plexClient)
{
    private const string BackupFileNameSuffix = ".original";

    public static bool TryBackupPoster(string sourcePath, string backupPath)
    {
        if (File.Exists(backupPath))
        {
            return false;
        }

        File.Copy(sourcePath, backupPath, overwrite: true);
        return true;
    }

    public static bool TryRestorePoster(string backupPath, string targetPath)
    {
        if (!File.Exists(backupPath))
        {
            return false;
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        File.Delete(backupPath);
        return true;
    }

    public async Task<FileInfo?> GetAndBackupOriginalPoster(AssetPathInfo backupAsset, AssetPathInfo asset,
        string mediaFileName, string originalPlexPosterUrl)
    {
        // Will get poster.original.jpg first, else poster.jpg if exists, or null
        var originalPoster =
            GetOriginalPoster(backupAsset, asset, mediaFileName);
        var originalPosterBackupFullName = backupAsset.File.FullName + BackupFileNameSuffix;

        if (originalPoster is { Exists: true })
        {
            originalPosterBackupFullName = Path.ChangeExtension(originalPosterBackupFullName, originalPoster.Extension);

            if (!File.Exists(originalPosterBackupFullName))
                File.Copy(originalPoster.FullName, originalPosterBackupFullName, overwrite: true);

            return new FileInfo(originalPosterBackupFullName);
        }

        // download original poster from plex instead
        var plexPoster = await plexClient.DownloadPlexImageAsync(originalPlexPosterUrl);

        if (plexPoster is not { Exists: true } || !plexPoster.IsImageByExtension())
        {
            return null;
        }

        var mediaFileFullName = Path.ChangeExtension(asset.FileName, plexPoster.Extension);
        originalPosterBackupFullName = Path.ChangeExtension(originalPosterBackupFullName, plexPoster.Extension);

        File.Copy(plexPoster.FullName, mediaFileFullName, overwrite: true);
        File.Copy(plexPoster.FullName, originalPosterBackupFullName, overwrite: true);
        File.Delete(plexPoster.FullName);

        return new FileInfo(originalPosterBackupFullName);
    }

    private static FileInfo? GetOriginalPoster(AssetPathInfo backupAsset, AssetPathInfo asset,
        string mediaFileName)
    {
        // check for backup first - if exists it should be the best copy
        var backupFileList = backupAsset.Directory?.GetFiles() ?? [];

        var backupMatches = backupFileList.Where(fileInfo =>
                fileInfo.Exists &&
                fileInfo.Name.StartsWith(mediaFileName) &&
                fileInfo.IsImageByExtension())
            .ToList();

        // look at asset directory second
        var fileList = asset.Directory?.GetFiles() ?? [];

        var backupOriginalPoster = backupMatches.FirstOrDefault(x => x.Name.Contains(BackupFileNameSuffix));

        var matches = fileList.Where(fileInfo => fileInfo.Exists &&
                                                 fileInfo.Name.StartsWith(mediaFileName) &&
                                                 fileInfo.IsImageByExtension())
            .ToList();
        return backupOriginalPoster ?? matches.FirstOrDefault();
    }
}
