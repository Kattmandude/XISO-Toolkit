using XISO.Core.Models;

namespace XISO.Core.Metadata;

public class Xbox360MetadataReader : IGameMetadataReader
{
    private readonly XexReader _xexReader = new();

    public bool CanRead(string executablePath)
    {
        return Path.GetFileName(executablePath)
            .Equals("default.xex", StringComparison.OrdinalIgnoreCase);
    }

    public void ReadMetadata(string executablePath, GameImageInfo info)
    {
        info.Executable = "default.xex";

        if (_xexReader.IsValidXex(executablePath))
        {
            info.Version = "Valid XEX2 executable";
        }
    }
}