using Yamoh.Domain.State;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetManager
{
    public bool TryBackupPoster(string sourcePath, string backupPath)
    {
        if (File.Exists(backupPath))
        {
            return false;
        }

        File.Copy(sourcePath, backupPath, overwrite: true);
        return true;
    }

    public bool TryRestorePoster(string backupPath, string targetPath)
    {
        if (!File.Exists(backupPath))
        {
            return false;
        }

        File.Copy(backupPath, targetPath, overwrite: true);
        File.Delete(backupPath);
        return true;
    }
}
