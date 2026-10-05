using System.Text.Json;
using System.Text.Json.Serialization;
using XISO.Core.Detection;
using XISO.Core.Models;

namespace XISO.Core.Data;

public sealed class GameTitleDatabase
{
    private readonly List<GameTitleRecord> _records;

    public GameTitleDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "The game title database was not found.",
                databasePath);
        }

        var json = File.ReadAllText(databasePath);

        _records =
            JsonSerializer.Deserialize<List<GameTitleRecord>>(
                json,
                new JsonSerializerOptions
                {
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    }
                })
            ?? [];
    }

    public GameTitleRecord? Find(
        XboxPlatform platform,
        string titleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);

        return _records.FirstOrDefault(record =>
            record.Platform == platform &&
            string.Equals(
                record.TitleId,
                titleId,
                StringComparison.OrdinalIgnoreCase));
    }
}