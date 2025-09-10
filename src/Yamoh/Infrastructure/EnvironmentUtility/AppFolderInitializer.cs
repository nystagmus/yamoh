using Serilog;

namespace Yamoh.Infrastructure.EnvironmentUtility;

public class AppFolderInitializer(AppEnvironment env)
{
    public record PermissionCheckResult(string Path, bool Successful, string Message);

    public void Initialize()
    {
        Directory.CreateDirectory(env.ConfigFolder);
        Directory.CreateDirectory(env.DefaultsFolder);
        Directory.CreateDirectory(env.StateFolder);
    }

    public List<PermissionCheckResult> CheckRequiredFolderPermissions()
    {
        Log.Debug("Checking Directory permissions...");
        var results = new List<PermissionCheckResult>();

        results.Add(HasPermissions(env.ConfigFolder, includeWrite: true));
        results.Add(HasPermissions(env.DefaultsFolder, includeWrite: false));
        results.Add(HasPermissions(env.StateFolder, includeWrite: true));
        results.Add(HasPermissions(env.LogFolder, includeWrite: true));

        return results;
    }

    private PermissionCheckResult HasPermissions(string path, bool includeWrite = false)
    {
        try
        {
            // Test read
            var files = Directory.EnumerateFiles(path).Take(1).Any();

            if (includeWrite)
            {
                // Test write
                var testFile = Path.Combine(path, "permission_test.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
        }
        catch (Exception ex)
        {
            return new PermissionCheckResult(path, false, ex.Message);
        }

        return new PermissionCheckResult(path, true, "No errors");
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
