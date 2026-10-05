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

        _xexReader.ReadMetadata(executablePath, info);
    }
}