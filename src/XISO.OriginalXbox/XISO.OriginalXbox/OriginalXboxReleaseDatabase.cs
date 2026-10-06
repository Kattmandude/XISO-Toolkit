using Microsoft.Data.Sqlite;

namespace XISO.OriginalXbox;

public sealed class OriginalXboxReleaseDatabase
{
    private readonly string _databasePath;

    public OriginalXboxReleaseDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "The Original Xbox release database was not found.",
                databasePath);
        }

        _databasePath = databasePath;
    }

    public OriginalXboxReleaseRecord? FindByXbeMd5(
        string xbeMd5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xbeMd5);

        using var connection =
            new SqliteConnection(
                $"Data Source={_databasePath}");

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                XbeMd5,
                TitleId,
                SerialNumber,
                Xmid,
                FullName,
                TitleName,
                Region,
                Version
            FROM Releases
            WHERE XbeMd5 = $xbeMd5
            LIMIT 1;
            """;

        command.Parameters.AddWithValue(
            "$xbeMd5",
            xbeMd5);

        using var reader =
            command.ExecuteReader();

        if (!reader.Read())
            return null;

        return new OriginalXboxReleaseRecord
        {
            XbeMd5 = reader.GetString(0),
            TitleId = reader.GetString(1),
            SerialNumber = reader.IsDBNull(2)
                ? string.Empty
                : reader.GetString(2),
            Xmid = reader.IsDBNull(3)
                ? string.Empty
                : reader.GetString(3),
            FullName = reader.IsDBNull(4)
                ? string.Empty
                : reader.GetString(4),
            TitleName = reader.IsDBNull(5)
                ? string.Empty
                : reader.GetString(5),
            Region = reader.IsDBNull(6)
                ? string.Empty
                : reader.GetString(6),
            Version = reader.IsDBNull(7)
                ? string.Empty
                : reader.GetString(7)
        };
    }
}
