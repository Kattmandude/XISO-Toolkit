using XISO.Core.Detection;

namespace XISO.Core.Data;

public interface IGameTitleProvider
{
    GameTitleLookupResult? Find(
        XboxPlatform platform,
        string titleId,
        string mediaId);

    GameTitleLookupResult? FindByTitleId(
        XboxPlatform platform,
        string titleId);
}