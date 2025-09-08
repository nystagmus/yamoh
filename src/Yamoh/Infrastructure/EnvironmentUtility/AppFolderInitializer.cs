using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Serilog;

namespace Yamoh.Infrastructure.EnvironmentUtility;

public class AppFolderInitializer(AppEnvironment env)
{
    public record PermissionCheckFailureResult(string Path, string Message);

    public void Initialize()
    {
        Directory.CreateDirectory(env.ConfigFolder);
        Directory.CreateDirectory(env.DefaultsFolder);
        Directory.CreateDirectory(env.StateFolder);
    }

    public List<PermissionCheckFailureResult> CheckRequiredFolderPermissions()
    {
        Log.Debug("Checking Directory permissions...");
        var results = new List<PermissionCheckFailureResult>();

        foreach (var folder in env.Folders)
        {
            try
            {
                // Test read
                var files = Directory.GetFiles(folder);

                // Test write
                var testFile = Path.Combine(folder, "permission_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                results.Add(new PermissionCheckFailureResult(folder, ex.Message));
            }
        }

        return results;
    }

    public void CopyDefaultsIfMissing()
    {
        Log.Debug("Creating default configuration (if missing)...");

        foreach (var file in Directory.GetFiles(env.DefaultsFolder))
        {
            var destFile = Path.Combine(env.ConfigFolder, Path.GetFileName(file));

            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }

        foreach (var dir in Directory.GetDirectories(env.DefaultsFolder))
        {
            var destDir = Path.Combine(env.ConfigFolder, Path.GetFileName(dir));

            if (!Directory.Exists(destDir))
            {
                DirectoryCopy(dir, destDir, true);
            }
        }
    }

    private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;
        Directory.CreateDirectory(destDir);

        foreach (var file in dir.GetFiles())
            file.CopyTo(Path.Combine(destDir, file.Name), false);

        if (!copySubDirs)
        {
            return;
        }

        foreach (var subDir in dir.GetDirectories())
            DirectoryCopy(subDir.FullName, Path.Combine(destDir, subDir.Name), copySubDirs);
    }
}
