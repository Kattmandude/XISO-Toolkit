using XISO.Core.Detection;
using XISO.Core.Metadata;
using XISO.Core.Models;

namespace XISO.Core.Analysis;

public class ImageAnalyzer : IImageAnalyzer
{
    private readonly IsoPlatformDetector _detector;

    private readonly List<IGameMetadataReader> _readers =
    [
        new Xbox360MetadataReader(),
        new OriginalXboxMetadataReader()
    ];

    public ImageAnalyzer()
    {
        _detector = new IsoPlatformDetector();
    }

    public GameImageInfo Analyze(string folderPath)
    {
        var info = new GameImageInfo
        {
            FilePath = folderPath,
            FileName = Path.GetFileName(folderPath),
            FileSize = GetFolderSize(folderPath)
        };

        info.Platform = _detector.Detect(folderPath);

        info.Executable = info.Platform switch
        {
            XboxPlatform.Xbox360 => "default.xex",
            XboxPlatform.OriginalXbox => "default.xbe",
            _ => string.Empty
        };

        info.Title = Path.GetFileName(folderPath);

        if (!string.IsNullOrEmpty(info.Executable))
        {
            var executablePath = Path.Combine(folderPath, info.Executable);

            foreach (var reader in _readers)
            {
                if (reader.CanRead(executablePath))
                {
                    reader.ReadMetadata(executablePath, info);
                    break;
                }
            }
        }

        return info;
    }

    private static long GetFolderSize(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return 0;

        return Directory
            .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }
}