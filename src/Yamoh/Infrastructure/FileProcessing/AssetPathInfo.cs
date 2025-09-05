using Yamoh.Features.OverlayManager;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetPathInfo
{
    public AssetPathInfo(string basePath, PlexMetadataBuilderItem item)
    {
        FilePath = Path.GetFullPath(Path.Combine(basePath, item.MediaFileRelativePath));
        FileName = Path.GetFullPath(Path.Combine(FilePath, item.MediaFileName));
    }

    public void UpdateExtension(FileInfo fileInfo)
    {
        FileName = Path.ChangeExtension(FileName, fileInfo.Extension);
    }
    public string FilePath { get; init; }
    public string FileName { get; private set; }

    public FileInfo File => new(Path.Combine(FilePath, FileName));

    public DirectoryInfo? Directory => File.Directory;
}
