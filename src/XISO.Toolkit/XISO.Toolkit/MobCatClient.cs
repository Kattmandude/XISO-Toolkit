using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XISO.Toolkit;

public sealed class MobCatClient
{
    private const string DatabaseUrl =
        "https://www.mobcat.zip/XboxIDs/titleIDs.db";

    private const string DatabaseFileName =
        "MobCatsOGXboxTitleIDs.db";

    private const string MetadataFileName =
        "MobCatsOGXboxTitleIDs.json";

    private readonly HttpClient _httpClient;

    public string DatabasePath { get; }

    private string MetadataPath { get; }

    public MobCatClient()
    {
        _httpClient = new HttpClient();

        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "XISO-Toolkit",
                "1.0"));

        string dataDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "XISO-Toolkit");

        Directory.CreateDirectory(dataDirectory);

        DatabasePath =
            Path.Combine(
                dataDirectory,
                DatabaseFileName);

        MetadataPath =
            Path.Combine(
                dataDirectory,
                MetadataFileName);
    }

    public async Task<bool> EnsureDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(DatabasePath))
            return true;

        try
        {
            await DownloadDatabaseAsync(
                false,
                cancellationToken);

            return File.Exists(DatabasePath);
        }
        catch
        {
            return false;
        }
    }

    public async Task<MobCatUpdateResult> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DatabasePath))
        {
            return new MobCatUpdateResult
            {
                DatabaseExists = false,
                UpdateAvailable = true,
                Reason = "Local MobCat database does not exist."
            };
        }

        LocalDatabaseInfo? localInfo =
            await LoadLocalDatabaseInfoAsync(
                cancellationToken);

        RemoteDatabaseInfo remoteInfo =
            await GetRemoteDatabaseInfoAsync(
                cancellationToken);

        if (localInfo == null)
        {
            return new MobCatUpdateResult
            {
                DatabaseExists = true,
                UpdateAvailable = true,
                Reason =
                    "Local database metadata does not exist. " +
                    "The database will be downloaded once to establish it."
            };
        }

        bool updateAvailable =
            IsUpdateAvailable(
                localInfo,
                remoteInfo);

        string reason;

        if (updateAvailable)
        {
            reason = BuildUpdateReason(
                localInfo,
                remoteInfo);
        }
        else
        {
            reason = "Local MobCat database is current.";
        }

        return new MobCatUpdateResult
        {
            DatabaseExists = true,
            UpdateAvailable = updateAvailable,
            Reason = reason,
            LocalInfo = localInfo,
            RemoteInfo = remoteInfo
        };
    }

    public async Task<MobCatUpdateResult> CheckForUpdateAndInstallAsync(
        CancellationToken cancellationToken = default)
    {
        MobCatUpdateResult result =
            await CheckForUpdateAsync(
                cancellationToken);

        if (!result.UpdateAvailable)
            return result;

        await DownloadDatabaseAsync(
            true,
            cancellationToken);

        return new MobCatUpdateResult
        {
            DatabaseExists = true,
            UpdateAvailable = false,
            Updated = true,
            Reason = result.Reason,
            LocalInfo =
                await LoadLocalDatabaseInfoAsync(
                    cancellationToken)
        };
    }

    public async Task DownloadDatabaseAsync(
        bool forceDownload,
        CancellationToken cancellationToken = default)
    {
        if (!forceDownload &&
            File.Exists(DatabasePath))
        {
            return;
        }

        string temporaryDatabasePath =
            DatabasePath + ".download";

        string temporaryMetadataPath =
            MetadataPath + ".download";

        TryDelete(temporaryDatabasePath);
        TryDelete(temporaryMetadataPath);

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    DatabaseUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using Stream sourceStream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            await using FileStream destinationStream =
                new FileStream(
                    temporaryDatabasePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 64,
                    FileOptions.SequentialScan);

            await sourceStream.CopyToAsync(
                destinationStream,
                cancellationToken);
        }
        catch
        {
            TryDelete(temporaryDatabasePath);
            throw;
        }

        try
        {
            int recordCount =
                await ValidateDatabaseAsync(
                    temporaryDatabasePath,
                    cancellationToken);

            RemoteDatabaseInfo remoteInfo;

            try
            {
                remoteInfo =
                    await GetRemoteDatabaseInfoAsync(
                        cancellationToken);
            }
            catch
            {
                remoteInfo =
                    new RemoteDatabaseInfo();
            }

            LocalDatabaseInfo metadata =
                new LocalDatabaseInfo
                {
                    DatabaseFileName =
                        DatabaseFileName,

                    DatabaseSize =
                        new FileInfo(
                            temporaryDatabasePath).Length,

                    RecordCount =
                        recordCount,

                    ETag =
                        remoteInfo.ETag,

                    LastModified =
                        remoteInfo.LastModified,

                    ContentLength =
                        remoteInfo.ContentLength,

                    InstalledUtc =
                        DateTimeOffset.UtcNow
                };

            await SaveLocalDatabaseInfoAsync(
                metadata,
                temporaryMetadataPath,
                cancellationToken);

            File.Move(
                temporaryDatabasePath,
                DatabasePath,
                true);

            File.Move(
                temporaryMetadataPath,
                MetadataPath,
                true);
        }
        catch
        {
            TryDelete(temporaryDatabasePath);
            TryDelete(temporaryMetadataPath);
            throw;
        }
    }

    public async Task<MobCatTitle?> GetTitleByXmidAsync(
        string xmid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmid))
            return null;

        bool databaseAvailable =
            await EnsureDatabaseAsync(
                cancellationToken);

        if (!databaseAvailable)
            return null;

        string normalizedXmid =
            NormalizeXmid(xmid);

        if (string.IsNullOrWhiteSpace(normalizedXmid))
            return null;

        string? sqlitePath =
            FindSqliteExecutable();

        if (sqlitePath == null)
        {
            throw new InvalidOperationException(
                "sqlite3.exe was not found. " +
                "MobCat database lookups require sqlite3.exe.");
        }

        string escapedXmid =
            normalizedXmid
                .Replace("'", "''");

        string sql =
            "SELECT * FROM TitleIDs " +
            $"WHERE LOWER(XMID) = LOWER('{escapedXmid}') " +
            "LIMIT 1;";

        string output =
            await RunSqliteAsync(
                sqlitePath,
                sql,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
            return null;

        return ParseTitle(
            output);
    }

    private async Task<RemoteDatabaseInfo> GetRemoteDatabaseInfoAsync(
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage headRequest =
            new HttpRequestMessage(
                HttpMethod.Head,
                DatabaseUrl);

        using HttpResponseMessage headResponse =
            await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (headResponse.IsSuccessStatusCode)
        {
            return CreateRemoteDatabaseInfo(
                headResponse);
        }

        /*
         * Some web servers do not support HEAD correctly.
         * Fall back to a normal GET request for headers only.
         */
        using HttpRequestMessage getRequest =
            new HttpRequestMessage(
                HttpMethod.Get,
                DatabaseUrl);

        using HttpResponseMessage getResponse =
            await _httpClient.SendAsync(
                getRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        getResponse.EnsureSuccessStatusCode();

        return CreateRemoteDatabaseInfo(
            getResponse);
    }

    private static RemoteDatabaseInfo CreateRemoteDatabaseInfo(
        HttpResponseMessage response)
    {
        return new RemoteDatabaseInfo
        {
            ETag =
                response.Headers.ETag?.Tag,

            LastModified =
                response.Content.Headers.LastModified,

            ContentLength =
                response.Content.Headers.ContentLength
        };
    }

    private static bool IsUpdateAvailable(
        LocalDatabaseInfo localInfo,
        RemoteDatabaseInfo remoteInfo)
    {
        /*
         * ETag is the strongest comparison when both sides
         * provide one.
         */
        if (!string.IsNullOrWhiteSpace(localInfo.ETag) &&
            !string.IsNullOrWhiteSpace(remoteInfo.ETag))
        {
            return !string.Equals(
                localInfo.ETag,
                remoteInfo.ETag,
                StringComparison.Ordinal);
        }

        /*
         * If ETag is unavailable, compare Last-Modified.
         */
        if (localInfo.LastModified.HasValue &&
            remoteInfo.LastModified.HasValue)
        {
            return
                remoteInfo.LastModified.Value >
                localInfo.LastModified.Value;
        }

        /*
         * Last fallback is file size.
         */
        if (localInfo.ContentLength.HasValue &&
            remoteInfo.ContentLength.HasValue)
        {
            return
                localInfo.ContentLength.Value !=
                remoteInfo.ContentLength.Value;
        }

        /*
         * If the server gave us no useful comparison metadata,
         * assume an update is needed rather than falsely claiming
         * the local database is current.
         */
        return true;
    }

    private static string BuildUpdateReason(
        LocalDatabaseInfo localInfo,
        RemoteDatabaseInfo remoteInfo)
    {
        if (!string.IsNullOrWhiteSpace(localInfo.ETag) &&
            !string.IsNullOrWhiteSpace(remoteInfo.ETag) &&
            !string.Equals(
                localInfo.ETag,
                remoteInfo.ETag,
                StringComparison.Ordinal))
        {
            return "MobCat database ETag has changed.";
        }

        if (localInfo.LastModified.HasValue &&
            remoteInfo.LastModified.HasValue &&
            remoteInfo.LastModified.Value >
            localInfo.LastModified.Value)
        {
            return
                $"MobCat database was modified " +
                $"on {remoteInfo.LastModified.Value:yyyy-MM-dd HH:mm:ss} UTC.";
        }

        if (localInfo.ContentLength.HasValue &&
            remoteInfo.ContentLength.HasValue &&
            localInfo.ContentLength.Value !=
            remoteInfo.ContentLength.Value)
        {
            return
                $"MobCat database size changed from " +
                $"{localInfo.ContentLength.Value:N0} bytes to " +
                $"{remoteInfo.ContentLength.Value:N0} bytes.";
        }

        return
            "MobCat database metadata changed or could not be compared.";
    }

    private async Task<int> ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException(
                "Downloaded MobCat database was not found.",
                databasePath);
        }

        FileInfo fileInfo =
            new FileInfo(databasePath);

        if (fileInfo.Length == 0)
        {
            throw new InvalidDataException(
                "Downloaded MobCat database is empty.");
        }

        string? sqlitePath =
            FindSqliteExecutable();

        if (sqlitePath == null)
        {
            throw new InvalidOperationException(
                "sqlite3.exe was not found. " +
                "The downloaded MobCat database could not be validated.");
        }

        string escapedPath =
            databasePath.Replace(
                "'",
                "''");

        string sql =
            "SELECT COUNT(*) FROM TitleIDs;";

        string output =
            await RunSqliteAsync(
                sqlitePath,
                sql,
                cancellationToken,
                escapedPath);

        string trimmed =
            output.Trim();

        if (!int.TryParse(
                trimmed,
                out int count) ||
            count <= 0)
        {
            throw new InvalidDataException(
                "Downloaded MobCat database does not contain a valid titles table.");
        }

        return count;
    }

    private async Task<LocalDatabaseInfo?> LoadLocalDatabaseInfoAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(MetadataPath))
            return null;

        try
        {
            string json =
                await File.ReadAllTextAsync(
                    MetadataPath,
                    cancellationToken);

            return JsonSerializer.Deserialize<LocalDatabaseInfo>(
                json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SaveLocalDatabaseInfoAsync(
        LocalDatabaseInfo info,
        string path,
        CancellationToken cancellationToken)
    {
        JsonSerializerOptions options =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        string json =
            JsonSerializer.Serialize(
                info,
                options);

        await File.WriteAllTextAsync(
            path,
            json,
            cancellationToken);
    }

    private async Task<string> RunSqliteAsync(
        string sqlitePath,
        string sql,
        CancellationToken cancellationToken,
        string? databaseOverride = null)
    {
        string databasePath =
            databaseOverride ?? DatabasePath;

        var startInfo =
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = sqlitePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        startInfo.ArgumentList.Add(
            databasePath);

        startInfo.ArgumentList.Add(
            "-noheader");

        startInfo.ArgumentList.Add(
            "-batch");

        startInfo.ArgumentList.Add(
            "-separator");

        startInfo.ArgumentList.Add(
            "|");

        startInfo.ArgumentList.Add(
            sql);

        using var process =
            new System.Diagnostics.Process
            {
                StartInfo = startInfo
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start sqlite3.exe.");
        }

        string standardOutput =
            await process.StandardOutput.ReadToEndAsync(
                cancellationToken);

        string standardError =
            await process.StandardError.ReadToEndAsync(
                cancellationToken);

        await process.WaitForExitAsync(
            cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError)
                    ? $"sqlite3.exe exited with code {process.ExitCode}."
                    : standardError.Trim());
        }

        return standardOutput;
    }

    private string? FindSqliteExecutable()
    {
        // Released application:
        // sqlite3.exe is bundled beside XISO.Toolkit.exe.
        string bundledPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "sqlite3.exe");

        if (File.Exists(bundledPath))
            return bundledPath;

        // Development fallback:
        // Search upward from the application directory.
        string? result =
            FindUpward(
                AppContext.BaseDirectory,
                "sqlite3.exe");

        if (result != null)
            return result;

        // Final fallback:
        // Allow sqlite3.exe to be supplied through PATH.
        string? pathEnvironment =
            Environment.GetEnvironmentVariable(
                "PATH");

        if (!string.IsNullOrWhiteSpace(pathEnvironment))
        {
            foreach (string directory in
                     pathEnvironment.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate =
                        Path.Combine(
                            directory,
                            "sqlite3.exe");

                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore invalid PATH entries.
                }
            }
        }

        return null;
    }

    private static string? FindUpward(
        string startDirectory,
        string fileName)
    {
        DirectoryInfo? directory;

        try
        {
            directory =
                new DirectoryInfo(
                    startDirectory);
        }
        catch
        {
            return null;
        }

        while (directory != null)
        {
            string candidate =
                Path.Combine(
                    directory.FullName,
                    fileName);

            if (File.Exists(candidate))
                return candidate;

            directory =
                directory.Parent;
        }

        return null;
    }

    private static MobCatTitle? ParseTitle(
        string output)
    {
        string line =
            output
                .Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(line))
            return null;

        string[] fields =
            line.Split('|');

        /*
         * MobCat's titleIDs.db has historically exposed these
         * fields in the following order:
         *
         *  0  Title ID
         *  1  Serial Number
         *  2  XMID
         *  3  Full Name
         *  4  Title Name
         *  5  Filename
         *  6  AKA
         *  7  Features
         *  8  Publisher
         *  9  Region
         * 10  XBE Rating
         * 11  Cover Rating
         * 12  Cover Stats
         * 13  UPC
         * 14  Version
         * 15  Media Type
         * 16  Init Flags
         * 17  Entry Point
         * 18  Cert Timestamp
         * 19  XDK Version
         * 20  Redump MD5
         * 21  XBE MD5
         */

        string Get(int index)
        {
            return index < fields.Length
                ? fields[index]
                : string.Empty;
        }

        return new MobCatTitle
        {
            TitleId = Get(0),
            SerialNumber = Get(1),
            Xmid = Get(2),
            FullName = Get(3),
            TitleName = Get(4),
            Filename = Get(5),
            Aka = Get(6),
            Features = Get(7),
            Publisher = Get(8),
            Region = Get(9),
            XbeRating = Get(10),
            CoverRating = Get(11),
            CoverStats = Get(12),
            Upc = Get(13),
            Version = Get(14),
            MediaType = Get(15),
            InitFlags = Get(16),
            EntryPoint = Get(17),
            CertTimestamp = Get(18),
            XdkVersion = Get(19),
            RedumpMd5 = Get(20),
            XbeMd5 = Get(21)
        };
    }

    private static string NormalizeXmid(
        string xmid)
    {
        string value =
            xmid.Trim();

        if (value.StartsWith(
                "0x",
                StringComparison.OrdinalIgnoreCase))
        {
            value =
                value[2..];
        }

        return value.ToUpperInvariant();
    }

    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}

public sealed class MobCatUpdateResult
{
    public bool DatabaseExists { get; init; }

    public bool UpdateAvailable { get; init; }

    public bool Updated { get; init; }

    public string Reason { get; init; } = string.Empty;

    public LocalDatabaseInfo? LocalInfo { get; init; }

    public RemoteDatabaseInfo? RemoteInfo { get; init; }
}

public sealed class LocalDatabaseInfo
{
    public string DatabaseFileName { get; init; } = string.Empty;

    public long DatabaseSize { get; init; }

    public int RecordCount { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public long? ContentLength { get; init; }

    public DateTimeOffset InstalledUtc { get; init; }
}

public sealed class RemoteDatabaseInfo
{
    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public long? ContentLength { get; init; }
}

public sealed class MobCatTitle
{
    public string TitleId { get; init; } = string.Empty;

    public string SerialNumber { get; init; } = string.Empty;

    public string Xmid { get; init; } = string.Empty;

    public string FullName { get; init; } = string.Empty;

    public string TitleName { get; init; } = string.Empty;

    public string Filename { get; init; } = string.Empty;

    public string Aka { get; init; } = string.Empty;

    public string Features { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string XbeRating { get; init; } = string.Empty;

    public string CoverRating { get; init; } = string.Empty;

    public string CoverStats { get; init; } = string.Empty;

    public string Upc { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string MediaType { get; init; } = string.Empty;

    public string InitFlags { get; init; } = string.Empty;

    public string EntryPoint { get; init; } = string.Empty;

    public string CertTimestamp { get; init; } = string.Empty;

    public string XdkVersion { get; init; } = string.Empty;

    public string RedumpMd5 { get; init; } = string.Empty;

    public string XbeMd5 { get; init; } = string.Empty;
}