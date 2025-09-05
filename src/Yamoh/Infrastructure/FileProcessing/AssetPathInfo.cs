using System.Text.Json.Serialization;
using Yamoh.Features.OverlayManager;
using static Yamoh.Infrastructure.FileProcessing.AssetConstants;

namespace Yamoh.Infrastructure.FileProcessing;

public class AssetPathInfo
{
    public AssetPathInfo(AssetPathInfo source)
    {
        FilePath = source.FilePath;
        FileName = source.FileName;
    }

    public AssetPathInfo(FileInfo fileInfo)
    {
        FilePath = fileInfo.Directory?.FullName ?? throw new DirectoryNotFoundException();
        FileName = fileInfo.Name;
    }

    public AssetPathInfo(string basePath, string fileName)
    {
        FilePath = basePath;
        FileName = fileName;
    }

    public AssetPathInfo(string basePath, PlexMetadataBuilderItem item)
    {
        FilePath = Path.GetFullPath(Path.Combine(basePath, item.MediaFileRelativePath));
        FileName = Path.GetFullPath(Path.Combine(FilePath, item.MediaFileName));
    }

    public void UpdateExtension(FileInfo fileInfo)
    {
        if (!string.IsNullOrEmpty(File.Extension) || FileName.EndsWith(BackupFileNameSuffix))
        {
            FileName += fileInfo.Extension;
        }
        else
        {
            FileName = Path.ChangeExtension(FileName, fileInfo.Extension);
        }
    }
    public string FilePath { get; init; }
    public string FileName { get; set; }

    [JsonIgnore]
    public FileInfo File => new(Path.Combine(FilePath, FileName));

    [JsonIgnore]
    public DirectoryInfo? Directory => File.Directory;
}
