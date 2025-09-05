using Yamoh.Infrastructure.Extensions;
using Yamoh.Infrastructure.External;
using static Yamoh.Infrastructure.FileProcessing.AssetConstants;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetManager(PlexClient plexClient)
{
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

    public async Task<AssetPathInfo?> GetAndBackupOriginalPoster(DirectoryInfo backupAssetPath, AssetPathInfo asset,
        string mediaFileName, string originalPlexPosterUrl)
    {
        // 1. Look for a poster.jpg in the asset directory if exists
        // 2. Look for a backup if it doesnt exist
        // 3. If neither exist download a new one from Plex and save it to asset path
        // 4. Return the backup path - we'll always be working with the backup (but not overwriting it).

        var originalPoster =
            GetOriginalPoster(backupAssetPath, asset, mediaFileName);

        var backupAssetFileName = asset.File.Name + BackupFileNameSuffix;
        var originalPosterBackupAsset = new AssetPathInfo(backupAssetPath.FullName, backupAssetFileName);

        if (originalPoster is { Exists: true })
        {
            originalPosterBackupAsset.UpdateExtension(originalPoster);

            if (!originalPosterBackupAsset.File.Exists)
            {
                originalPoster.CopyTo(originalPosterBackupAsset.File.FullName, overwrite: true);
            }
        }
        else
        {
            // download original poster from plex instead
            var plexPoster = await plexClient.DownloadPlexImageAsync(originalPlexPosterUrl);

            if (plexPoster is not { Exists: true } || !plexPoster.IsImageByExtension())
            {
                return null;
            }

            var mediaFileFullName = new AssetPathInfo(asset);
            mediaFileFullName.UpdateExtension(plexPoster);
            originalPosterBackupAsset.UpdateExtension(plexPoster);

            plexPoster.CopyTo(mediaFileFullName.File.FullName, overwrite: true);
            plexPoster.CopyTo(originalPosterBackupAsset.File.FullName, overwrite: true);
            plexPoster.Delete();
        }

        return originalPosterBackupAsset;
    }

    private static FileInfo? GetOriginalPoster(DirectoryInfo backupAssetPath, AssetPathInfo asset,
        string mediaFileName)
    {
        // check for backup first - if exists it should be the best copy
        var backupFileList = backupAssetPath.GetFiles();

        var backupMatches = backupFileList.Where(fileInfo =>
                fileInfo.Exists &&
                fileInfo.Name.StartsWith(mediaFileName) &&
                fileInfo.IsImageByExtension())
            .ToList();

        // look at asset directory second
        var fileList = asset.Directory?.GetFiles() ?? [];

        var backupOriginalPoster = backupMatches.FirstOrDefault(x => x.Name.Contains(BackupFileNameSuffix));

        var matches = fileList.Where(fileInfo =>
                fileInfo.Exists &&
                fileInfo.Name.StartsWith(mediaFileName) &&
                fileInfo.IsImageByExtension())
            .ToList();
        return backupOriginalPoster ?? matches.FirstOrDefault();
    }
}
