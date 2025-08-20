namespace YAMOH.Infrastructure;

public static class FileInfoExtensions
{
    public static bool IsImageByExtension(this FileInfo fileInfo)
    {
        var extension = fileInfo.Extension.ToLowerInvariant();
        var imageExtensions = new HashSet<string?>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
        };
        return imageExtensions.Contains(extension);
    }
}

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
