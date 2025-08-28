namespace Yamoh.Infrastructure.Extensions;

public static class DirectoryInfoExtensions
{
    public static bool TryCreate(this DirectoryInfo directoryInfo)
    {
        try
        {
            if (!directoryInfo.Exists)
            {
                directoryInfo.Create();
            }
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }
    public static bool HasWritePermissions(this DirectoryInfo directoryInfo)
    {
        try
        {
            if (!directoryInfo.Exists) return false;
            File.Create(directoryInfo.FullName + "temp.txt").Close();
            File.Delete(directoryInfo.FullName + "temp.txt");
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }
}
