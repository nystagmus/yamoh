using System.Runtime.InteropServices;

namespace Yamoh.Infrastructure.EnvironmentUtility;

public class AppEnvironment
{
    public string ConfigFolder { get; }
    public string DefaultsFolder { get; }
    public string LogFolder { get; }
    public string StateFolder { get; }
    public bool IsDocker { get; }
    public string OsName { get; }

    public AppEnvironment()
    {
        OsName = GetOsName();
        IsDocker = DetectDocker();

        if (IsDocker)
        {
            ConfigFolder = "/config/";
        }
        else
        {
            var baseFolder = GetBaseAppDataFolder();
            ConfigFolder = Path.Combine(baseFolder, "Config");
        }

        LogFolder = Path.Combine(ConfigFolder, "Logs");
        StateFolder = Path.Combine(ConfigFolder, "State");
        DefaultsFolder = Path.Combine(AppContext.BaseDirectory, "Defaults");
    }

    private string GetOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "OSX";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        return "Unknown";
    }

    private bool DetectDocker()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;

        return File.Exists("/.dockerenv") ||
               (File.Exists("/proc/1/cgroup") && File.ReadAllText("/proc/1/cgroup").Contains("/docker/"));
    }

    private string GetBaseAppDataFolder()
    {
        var basePath = Environment.GetFolderPath(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.SpecialFolder.CommonApplicationData
                : Environment.SpecialFolder.ApplicationData);
        return Path.Combine(basePath, "YAMOH");
    }
}