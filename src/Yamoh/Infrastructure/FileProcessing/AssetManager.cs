using Yamoh.Domain.State;
using Yamoh.Infrastructure.Extensions;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetManager
{
    public const string BackupFileNameSuffix = ".original";

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

    public static FileInfo? GetOriginalPoster(DirectoryInfo backupDirectory, DirectoryInfo mediaFileDirectory,
        string mediaFileName)
    {
        var backupFileList = backupDirectory.GetFiles();

        var backupMatches = backupFileList.Where(fileInfo => fileInfo.Exists &&
                                                             fileInfo.Name.StartsWith(mediaFileName) &&
                                                             fileInfo.IsImageByExtension())
            .ToList();
        var fileList = mediaFileDirectory.GetFiles();
        var backupOriginalPoster = backupMatches.FirstOrDefault(x => x.Name.Contains(BackupFileNameSuffix));

        var matches = fileList.Where(fileInfo => fileInfo.Exists &&
                                                 fileInfo.Name.StartsWith(mediaFileName) &&
                                                 fileInfo.IsImageByExtension())
            .ToList();
        return backupOriginalPoster ?? matches.FirstOrDefault();
    }
}
