using System.Text.Json;
using System.Text.Json.Serialization;
using XISO.Core.Models;

namespace XISO.Core.Data;

public sealed class ProcessedIsoDatabase
{
    public List<ProcessedIsoRecord> Load(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath))
        {
            return [];
        }

        var json =
            File.ReadAllText(databasePath);

        return
            JsonSerializer.Deserialize<List<ProcessedIsoRecord>>(
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

    public void Save(
        string databasePath,
        List<ProcessedIsoRecord> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(records);

        var json =
            JsonSerializer.Serialize(
                records,
                new JsonSerializerOptions
                {
                    Converters =
                    {
                        new JsonStringEnumConverter()
                    },
                    WriteIndented = true
                });

        File.WriteAllText(
            databasePath,
            json);
    }
}