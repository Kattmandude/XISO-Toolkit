using XISO.Core.Detection;

namespace XISO.Core.Data;

public sealed class LocalGameTitleProvider : IGameTitleProvider
{
    private readonly GameTitleDatabase _database;

    public LocalGameTitleProvider(string databasePath)
    {
        _database =
            new GameTitleDatabase(databasePath);
    }

    public GameTitleLookupResult? FindByTitleId(
        XboxPlatform platform,
        string titleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);

        var titleRecord =
            _database.Find(
                platform,
                titleId);

        if (titleRecord is null)
            return null;

        return new GameTitleLookupResult
        {
            Title = titleRecord.Title
        };
    }

    public GameTitleLookupResult? Find(
        XboxPlatform platform,
        string titleId,
        string mediaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);

        var titleRecord =
            _database.Find(
                platform,
                titleId);

        if (titleRecord is null)
            return null;

        var release =
            titleRecord.Releases.FirstOrDefault(
                release =>
                    string.Equals(
                        release.MediaId,
                        mediaId,
                        StringComparison.OrdinalIgnoreCase));

        if (release is null)
            return null;

        return new GameTitleLookupResult
        {
            Title = titleRecord.Title,
            Region = release.Region
        };
    }
}