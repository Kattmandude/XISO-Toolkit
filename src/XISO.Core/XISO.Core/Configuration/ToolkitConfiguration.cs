using XISO.Core.Models;

namespace XISO.Core.Configuration;

public sealed class ToolkitConfiguration
{
    public List<GameLibrary> GameLibraries { get; set; } = [];

    public string LastImageDirectory { get; set; } = string.Empty;
}
