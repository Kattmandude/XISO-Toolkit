using XISO.Core.Detection;

namespace XISO.Core.Models;

public sealed class GameTitleRecord
{
    public XboxPlatform Platform { get; set; }

    public string TitleId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<GameReleaseRecord> Releases { get; set; } = [];
}
