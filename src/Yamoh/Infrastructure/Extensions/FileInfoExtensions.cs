namespace Yamoh.Infrastructure.Extensions;

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