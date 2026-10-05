using XISO.Core.Models;

namespace XISO.Core.Metadata;

public class OriginalXboxMetadataReader : IGameMetadataReader
{
    public bool CanRead(string executablePath)
    {
        return Path.GetFileName(executablePath)
            .Equals("default.xbe", StringComparison.OrdinalIgnoreCase);
    }

    public void ReadMetadata(string executablePath, GameImageInfo info)
    {
        info.Executable = "default.xbe";

        // XBE parsing will be added here.
    }
}