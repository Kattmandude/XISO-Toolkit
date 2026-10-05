using XISO.Core.Detection;

namespace XISO.Core.Models;

public class GameImageInfo
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public XboxPlatform Platform { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Executable { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string TitleId { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}