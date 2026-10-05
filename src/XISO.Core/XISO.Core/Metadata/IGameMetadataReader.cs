using XISO.Core.Models;

namespace XISO.Core.Metadata;

public interface IGameMetadataReader
{
    bool CanRead(string executablePath);

    void ReadMetadata(string executablePath, GameImageInfo info);
}