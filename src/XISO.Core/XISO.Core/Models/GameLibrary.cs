using XISO.Core.Detection;

namespace XISO.Core.Models;

public sealed class GameLibrary
{
    public string Name { get; set; } = string.Empty;
    public XboxPlatform Platform { get; set; }
    public string Path { get; set; } = string.Empty;
}