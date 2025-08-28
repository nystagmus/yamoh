using Serilog;

namespace Yamoh.Infrastructure.EnvironmentUtility;

public class AppFolderInitializer(AppEnvironment env)
{
    public void Initialize()
    {
        Directory.CreateDirectory(env.ConfigFolder);
        Directory.CreateDirectory(env.DefaultsFolder);
    }

    public bool CheckPermissions()
    {
        try
        {
            Log.Debug("Checking Directory permissions...");
            var testFile = Path.Combine(env.ConfigFolder, "permission_test.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            // Test read
            var files = Directory.GetFiles(env.ConfigFolder);
            return true;
        }
        catch
        {
            return false;
        }
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