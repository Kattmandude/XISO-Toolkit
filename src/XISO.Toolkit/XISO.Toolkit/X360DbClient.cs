using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XISO.Toolkit;

public sealed class X360DbClient
{
    private const string PrimaryBaseUrl =
    "https://xenia-manager.github.io/x360db/";

    private const string FallbackBaseUrl =
        "https://raw.githubusercontent.com/xenia-manager/x360db/main/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public string DatabaseDirectory { get; }

    public string IndexPath { get; }

    public string DetailsDirectory { get; }

    public string ArtworkDirectory { get; }

    public X360DbClient()
    {
        DatabaseDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "XISO-Toolkit",
            "X360Db");

        IndexPath = Path.Combine(
            DatabaseDirectory,
            "games.json");

        DetailsDirectory = Path.Combine(
            DatabaseDirectory,
            "Titles");

        ArtworkDirectory = Path.Combine(
            DatabaseDirectory,
            "Artwork");

        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(DetailsDirectory);
        Directory.CreateDirectory(ArtworkDirectory);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "XISO-Toolkit/1.0");
    }

    public async Task<X360DbUpdateResult> CheckForUpdateAndInstallAsync(
        CancellationToken cancellationToken = default)
    {
        string temporaryPath =
            IndexPath + ".download";

        try
        {
            byte[] downloadedData =
                await DownloadIndexAsync(cancellationToken);

            ValidateIndex(downloadedData);

            string downloadedHash =
                Convert.ToHexString(
                    SHA256.HashData(downloadedData));

            if (File.Exists(IndexPath))
            {
                byte[] currentData =
                    await File.ReadAllBytesAsync(
                        IndexPath,
                        cancellationToken);

                try
                {
                    ValidateIndex(currentData);

                    string currentHash =
                        Convert.ToHexString(
                            SHA256.HashData(currentData));

                    if (string.Equals(
                        downloadedHash,
                        currentHash,
                        StringComparison.Ordinal))
                    {
                        return new X360DbUpdateResult(
                            false,
                            "Xbox 360 database is already up to date.",
                            CountRecords(currentData));
                    }
                }
                catch (Exception ex) when (
                    ex is JsonException or InvalidDataException)
                {
                    Console.WriteLine(
                        "The existing Xbox 360 index is invalid; replacing it with a validated copy.");
                }
            }

            await File.WriteAllBytesAsync(
                temporaryPath,
                downloadedData,
                cancellationToken);

            byte[] savedData =
                await File.ReadAllBytesAsync(
                    temporaryPath,
                    cancellationToken);

            ValidateIndex(savedData);

            File.Move(
                temporaryPath,
                IndexPath,
                true);

            return new X360DbUpdateResult(
                true,
                "Xbox 360 database updated successfully.",
                CountRecords(savedData));
        }
        catch (Exception ex)
        {
            return new X360DbUpdateResult(
                false,
                $"Xbox 360 database update failed: {ex.Message}",
                GetLocalRecordCount());
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public async Task<bool> EnsureDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(IndexPath))
        {
            try
            {
                byte[] existingData =
                    await File.ReadAllBytesAsync(
                        IndexPath,
                        cancellationToken);

                ValidateIndex(existingData);

                return true;
            }
            catch (Exception ex) when (
                ex is IOException or JsonException or InvalidDataException)
            {
                // Try to restore a valid index from the online source.
            }
        }

        X360DbUpdateResult result =
            await CheckForUpdateAndInstallAsync(
                cancellationToken);

        return result.RecordCount > 0 &&
               File.Exists(IndexPath);
    }

    public async Task<X360DbGame?> GetGameAsync(
        string titleId,
        string? mediaId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        bool databaseAvailable =
            await EnsureDatabaseAsync(cancellationToken);

        if (!databaseAvailable)
        {
            return null;
        }

        List<X360DbGame> games =
            await ReadIndexAsync(cancellationToken);

        string normalizedTitleId =
            NormalizeIdentifier(titleId);

        string normalizedMediaId =
            NormalizeIdentifier(mediaId);

        X360DbGame? game =
            games.FirstOrDefault(item =>
                NormalizeIdentifier(item.Id) == normalizedTitleId);

        if (game == null)
        {
            game = games.FirstOrDefault(item =>
                item.AlternativeId?.Any(id =>
                    NormalizeIdentifier(id) == normalizedTitleId) == true);
        }

        if (game == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(normalizedMediaId))
        {
            bool mediaIdFound =
                game.MediaId?.Any(id =>
                    NormalizeIdentifier(id) == normalizedMediaId) == true;

            if (!mediaIdFound)
            {
                X360DbGameInfo? details =
                    await GetDetailsAsync(
                        game.Id ?? normalizedTitleId,
                        cancellationToken);

                mediaIdFound =
                    details?.Media?.Any(item =>
                        NormalizeIdentifier(item.MediaId) == normalizedMediaId) == true;

                if (!mediaIdFound)
                {
                    return null;
                }
            }
        }

        return game;
    }

    public async Task<X360DbGameInfo?> GetDetailsAsync(
        string titleId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        string normalizedTitleId =
            NormalizeIdentifier(titleId);

        string cachedPath =
            GetDetailsPath(normalizedTitleId);

        if (File.Exists(cachedPath))
        {
            try
            {
                byte[] cachedData =
                    await File.ReadAllBytesAsync(
                        cachedPath,
                        cancellationToken);

                X360DbGameInfo? cachedInfo =
                    JsonSerializer.Deserialize<X360DbGameInfo>(
                        cachedData,
                        JsonOptions);

                if (IsValidDetails(cachedInfo))
                {
                    return cachedInfo;
                }
            }
            catch (Exception ex) when (
                ex is IOException or JsonException)
            {
                // A bad cached record should not prevent an online retry.
            }
        }

        string encodedTitleId =
            Uri.EscapeDataString(normalizedTitleId);

        byte[]? downloadedData =
            await TryDownloadAsync(
                $"titles/{encodedTitleId}/info.json",
                cancellationToken);

        if (downloadedData == null)
        {
            downloadedData =
                await TryDownloadAsync(
                    $"titles/{encodedTitleId}/info.json",
                    cancellationToken,
                    useFallback: true);
        }

        if (downloadedData == null)
        {
            return null;
        }

        X360DbGameInfo? details;

        try
        {
            details =
                JsonSerializer.Deserialize<X360DbGameInfo>(
                    downloadedData,
                    JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (!IsValidDetails(details))
        {
            return null;
        }

        string temporaryPath =
            cachedPath + ".download";

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                downloadedData,
                cancellationToken);

            File.Move(
                temporaryPath,
                cachedPath,
                true);
        }
        catch (IOException)
        {
            // The record is still usable for this analysis.
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }

        return details;
    }

    public async Task<string?> DownloadBoxArtAsync(
        string titleId,
        string? boxArtUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(titleId))
        {
            return null;
        }

        string normalizedTitleId =
            NormalizeIdentifier(titleId);

        string cachedPath =
            Path.Combine(
                ArtworkDirectory,
                normalizedTitleId + ".jpg");

        if (File.Exists(cachedPath))
        {
            return cachedPath;
        }

        var candidateUrls =
            new List<string>();

        if (Uri.TryCreate(
            boxArtUrl,
            UriKind.Absolute,
            out Uri? suppliedUri) &&
            (suppliedUri.Scheme == Uri.UriSchemeHttps ||
             suppliedUri.Scheme == Uri.UriSchemeHttp))
        {
            candidateUrls.Add(suppliedUri.ToString());
        }

        string encodedTitleId =
            Uri.EscapeDataString(normalizedTitleId);

        candidateUrls.Add(
            $"{PrimaryBaseUrl}titles/{encodedTitleId}/artwork/boxart.jpg");

        candidateUrls.Add(
            $"{FallbackBaseUrl}titles/{encodedTitleId}/artwork/boxart.jpg");

        foreach (string candidateUrl in candidateUrls.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using HttpResponseMessage response =
                    await _httpClient.GetAsync(
                        candidateUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                byte[] imageData =
                    await response.Content.ReadAsByteArrayAsync(
                        cancellationToken);

                if (!IsSupportedImage(imageData))
                {
                    continue;
                }

                string temporaryPath =
                    cachedPath + ".download";

                try
                {
                    await File.WriteAllBytesAsync(
                        temporaryPath,
                        imageData,
                        cancellationToken);

                    File.Move(
                        temporaryPath,
                        cachedPath,
                        true);
                }
                finally
                {
                    TryDeleteFile(temporaryPath);
                }

                return cachedPath;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or
                IOException or
                TaskCanceledException or
                InvalidDataException)
            {
                // Try the next artwork source.
            }
        }

        return null;
    }

    private async Task<byte[]> DownloadIndexAsync(
    CancellationToken cancellationToken)
    {
        string[] urls =
        [
            PrimaryBaseUrl + "games.json",
        FallbackBaseUrl + "games.json"
        ];

        List<string> failures = [];

        foreach (string url in urls)
        {
            try
            {
                using HttpResponseMessage response =
                    await _httpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    failures.Add(
                        $"{url} returned HTTP {(int)response.StatusCode} ({response.StatusCode}).");

                    continue;
                }

                byte[] data =
                    await response.Content.ReadAsByteArrayAsync(
                        cancellationToken);

                ValidateIndex(data);

                return data;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"{url} timed out.");
            }
            catch (HttpRequestException ex)
            {
                failures.Add($"{url}: {ex.Message}");
            }
        }

        throw new HttpRequestException(
            "Unable to download a valid x360db index. " +
            string.Join(" ", failures));
    }

    private async Task<byte[]?> TryDownloadAsync(
        string relativePath,
        CancellationToken cancellationToken,
        bool useFallback = false)
    {
        string baseUrl =
            useFallback
                ? FallbackBaseUrl
                : PrimaryBaseUrl;

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    baseUrl + relativePath,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<List<X360DbGame>> ReadIndexAsync(
        CancellationToken cancellationToken)
    {
        byte[] data =
            await File.ReadAllBytesAsync(
                IndexPath,
                cancellationToken);

        return JsonSerializer.Deserialize<List<X360DbGame>>(
                   data,
                   JsonOptions)
               ?? [];
    }

    private string GetDetailsPath(
        string normalizedTitleId)
    {
        return Path.Combine(
            DetailsDirectory,
            normalizedTitleId + ".json");
    }

    private int GetLocalRecordCount()
    {
        try
        {
            if (!File.Exists(IndexPath))
            {
                return 0;
            }

            return CountRecords(
                File.ReadAllBytes(IndexPath));
        }
        catch
        {
            return 0;
        }
    }

    private static void ValidateIndex(byte[] data)
    {
        using JsonDocument document =
            JsonDocument.Parse(data);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The x360db index is not a JSON array.");
        }

        if (document.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                "The x360db index contains no game records.");
        }

        JsonElement first =
            document.RootElement[0];

        if (first.ValueKind != JsonValueKind.Object ||
            !first.TryGetProperty("id", out _) ||
            !first.TryGetProperty("title", out _))
        {
            throw new InvalidDataException(
                "The x360db index does not contain the expected game fields.");
        }
    }

    private static int CountRecords(byte[] data)
    {
        using JsonDocument document =
            JsonDocument.Parse(data);

        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    private static bool IsValidDetails(
        X360DbGameInfo? details)
    {
        return details != null &&
               !string.IsNullOrWhiteSpace(details.Id) &&
               details.Title != null;
    }

    private static string NormalizeIdentifier(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized =
            value.Trim();

        if (normalized.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.ToUpperInvariant();
    }

    private static bool IsSupportedImage(byte[] data)
    {
        // JPEG signature.
        if (data.Length >= 3 &&
            data[0] == 0xFF &&
            data[1] == 0xD8 &&
            data[2] == 0xFF)
        {
            return true;
        }

        // PNG signature.
        return data.Length >= 8 &&
               data[0] == 0x89 &&
               data[1] == 0x50 &&
               data[2] == 0x4E &&
               data[3] == 0x47 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary-file cleanup is best effort.
        }
    }
}

public sealed class X360DbUpdateResult
{
    public bool Updated { get; }

    public string Reason { get; }

    public int RecordCount { get; }

    public X360DbUpdateResult(
        bool updated,
        string reason,
        int recordCount)
    {
        Updated = updated;
        Reason = reason;
        RecordCount = recordCount;
    }
}

public sealed class X360DbGame
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("alternative_id")]
    public List<string>? AlternativeId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("boxart")]
    public string? BoxArt { get; set; }

    [JsonPropertyName("media_id")]
    public List<string>? MediaId { get; set; }

    [JsonPropertyName("genre")]
    public List<string>? Genre { get; set; }

    [JsonPropertyName("user_rating")]
    public string? UserRating { get; set; }

    [JsonPropertyName("developer")]
    public string? Developer { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
}

public sealed class X360DbGameInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public X360DbTitle? Title { get; set; }

    [JsonPropertyName("genre")]
    public List<string>? Genre { get; set; }

    [JsonPropertyName("developer")]
    public string? Developer { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("user_rating")]
    public string? UserRating { get; set; }

    [JsonPropertyName("description")]
    public X360DbDescription? Description { get; set; }

    [JsonPropertyName("media")]
    public List<X360DbMedia>? Media { get; set; }

    [JsonPropertyName("artwork")]
    public X360DbArtwork? Artwork { get; set; }
}

public sealed class X360DbTitle
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }

    [JsonPropertyName("reduced")]
    public string? Reduced { get; set; }
}

public sealed class X360DbDescription
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }

    [JsonPropertyName("short")]
    public string? Short { get; set; }
}

public sealed class X360DbMedia
{
    [JsonPropertyName("media_id")]
    public string? MediaId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("edition")]
    public string? Edition { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }
}

public sealed class X360DbArtwork
{
    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("banner")]
    public string? Banner { get; set; }

    [JsonPropertyName("boxart")]
    public string? BoxArt { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("gallery")]
    public List<string>? Gallery { get; set; }
}
