using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XISO.Toolkit;

public sealed class TitleUpdateDownloader
{
    private const string ResourcesUrl =
        "https://xboxunity.net/Resources/Lib";

    private static readonly HttpClient Http = CreateHttpClient();

    public sealed record DownloadResult(
        int Found,
        int Downloaded,
        int Failed,
        IReadOnlyList<string> Messages);

    public sealed record AvailableUpdate(
        string Version,
        string FileName,
        string DownloadUrl);

    public sealed record UpdateSearchResult(
        bool Succeeded,
        IReadOnlyList<AvailableUpdate> Updates,
        string? Error);

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<UpdateSearchResult> CheckForUpdatesAsync(
        string titleId,
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        titleId = NormalizeId(titleId);
        mediaId = NormalizeId(mediaId);

        if (!IsHexId(titleId))
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                "The game's Title ID is invalid.");
        }

        if (!IsHexId(mediaId))
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                "The game's Media ID is invalid.");
        }

        try
        {
            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            string searchUrl =
                $"{ResourcesUrl}/TitleUpdateInfo.php?titleid=" +
                Uri.EscapeDataString(titleId);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                searchUrl);

            AddBrowserHeaders(request, includeAjaxHeader: true);

            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return new(
                    false,
                    Array.Empty<AvailableUpdate>(),
                    $"XboxUnity returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(timeout.Token);

            using var json = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeout.Token);

            var updates = ParseUpdates(
                json.RootElement,
                titleId,
                mediaId);

            return new(true, updates, null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                "The XboxUnity request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                $"Could not connect to XboxUnity: {ex.Message}");
        }
        catch (JsonException)
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                "XboxUnity returned an invalid JSON response.");
        }
        catch (IOException ex)
        {
            return new(
                false,
                Array.Empty<AvailableUpdate>(),
                $"Could not read the XboxUnity response: {ex.Message}");
        }
    }

    public async Task<DownloadResult> DownloadAsync(
        string titleId,
        string mediaId,
        string destinationFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AvailableUpdate>? knownUpdates = null)
    {
        var messages = new List<string>();
        var updates = new List<AvailableUpdate>();

        int downloaded = 0;
        int failed = 0;

        void Report(string message)
        {
            messages.Add(message);
            progress?.Report(message);
        }

        titleId = NormalizeId(titleId);
        mediaId = NormalizeId(mediaId);

        if (!IsHexId(titleId))
        {
            Report("Title Update download failed: invalid Title ID.");
            return new(0, 0, 1, messages);
        }

        if (!IsHexId(mediaId))
        {
            Report("Title Update download failed: invalid Media ID.");
            return new(0, 0, 1, messages);
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            Report("Title Update download failed: destination folder is empty.");
            return new(0, 0, 1, messages);
        }

        if (knownUpdates is not null)
        {
            updates.AddRange(knownUpdates);
            Report($"Using {updates.Count} previously discovered Title Update(s).");
        }
        else
        {
            Report($"Searching XboxUnity for Title ID {titleId}...");

            UpdateSearchResult search;

            try
            {
                search = await CheckForUpdatesAsync(
                    titleId,
                    mediaId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                Report("Title Update search was cancelled.");
                return new(0, 0, 1, messages);
            }

            if (!search.Succeeded)
            {
                Report(search.Error ?? "Title Update search failed.");
                return new(0, 0, 1, messages);
            }

            updates.AddRange(search.Updates);
            Report($"Found {updates.Count} matching Title Update(s).");
        }

        if (updates.Count == 0)
        {
            Report("No matching Title Updates are available.");
            return new(0, 0, 0, messages);
        }

        try
        {
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            Report($"Could not create the TitleUpdate folder: {ex.Message}");
            return new(updates.Count, 0, updates.Count, messages);
        }

        foreach (var update in updates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Report("Title Update downloads were cancelled.");
                failed += updates.Count - downloaded - failed;
                break;
            }

            string? temporaryPath = null;

            try
            {
                using var downloadTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                downloadTimeout.CancelAfter(TimeSpan.FromSeconds(60));

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    update.DownloadUrl);

                AddBrowserHeaders(request, includeAjaxHeader: false);

                using var response = await Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    downloadTimeout.Token);

                if (!response.IsSuccessStatusCode)
                {
                    Report(
                        $"Download failed for {update.FileName}: " +
                        $"HTTP {(int)response.StatusCode}.");

                    failed++;
                    continue;
                }

                string actualFileName = GetSafeFileName(
                    response.Content.Headers.ContentDisposition,
                    update.FileName);

                string destinationPath = Path.Combine(
                    destinationFolder,
                    actualFileName);

                temporaryPath = Path.Combine(
                    destinationFolder,
                    $".{Guid.NewGuid():N}.tmp");

                await using (var input =
                    await response.Content.ReadAsStreamAsync(
                        downloadTimeout.Token))
                await using (var output = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    useAsync: true))
                {
                    await input.CopyToAsync(
                        output,
                        8192,
                        downloadTimeout.Token);
                }

                cancellationToken.ThrowIfCancellationRequested();

                File.Move(
                    temporaryPath,
                    destinationPath,
                    overwrite: true);

                temporaryPath = null;
                downloaded++;

                Report($"Downloaded {actualFileName}.");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                Report("Title Update downloads were cancelled.");
                failed += updates.Count - downloaded - failed;
                break;
            }
            catch (OperationCanceledException)
            {
                Report($"Download timed out for {update.FileName}.");
                failed++;
            }
            catch (HttpRequestException)
            {
                Report($"Network error downloading {update.FileName}.");
                failed++;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                Report($"Could not save {update.FileName}: {ex.Message}");
                failed++;
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        // Cleanup failure must not hide the download result.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Cleanup failure must not hide the download result.
                    }
                }
            }
        }

        Report(
            $"Title Update summary: {updates.Count} found, " +
            $"{downloaded} downloaded, {failed} failed.");

        return new(updates.Count, downloaded, failed, messages);
    }

    private static List<AvailableUpdate> ParseUpdates(
        JsonElement root,
        string titleId,
        string mediaId)
    {
        var result = new List<AvailableUpdate>();

        if (root.ValueKind != JsonValueKind.Object)
            return result;

        int type = GetInt32(root, "Type");

        if (type == 1 &&
            root.TryGetProperty("MediaIDS", out var mediaItems) &&
            mediaItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var mediaItem in mediaItems.EnumerateArray())
            {
                string itemMediaId =
                    NormalizeId(GetString(mediaItem, "MediaID"));

                if (!string.Equals(
                    itemMediaId,
                    mediaId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (mediaItem.TryGetProperty("Updates", out var items))
                    AddUpdates(result, items, titleId);
            }
        }
        else if (type == 2 &&
                 root.TryGetProperty("Updates", out var items) &&
                 items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                string itemMediaId =
                    NormalizeId(GetString(item, "MediaID"));

                if (!string.Equals(
                    itemMediaId,
                    mediaId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddUpdate(result, item, titleId);
            }
        }

        return result
            .GroupBy(
                update => update.FileName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddUpdates(
        List<AvailableUpdate> result,
        JsonElement items,
        string titleId)
    {
        if (items.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in items.EnumerateArray())
            AddUpdate(result, item, titleId);
    }

    private static void AddUpdate(
        List<AvailableUpdate> result,
        JsonElement item,
        string titleId)
    {
        string updateId = GetString(item, "TitleUpdateID");
        string version = GetString(item, "Version");

        if (string.IsNullOrWhiteSpace(updateId) ||
            string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        string url =
            $"{ResourcesUrl}/TitleUpdate.php?tuid=" +
            Uri.EscapeDataString(updateId);

        result.Add(new AvailableUpdate(
            version,
            $"{titleId}_{version}.tu",
            url));
    }

    private static void AddBrowserHeaders(
        HttpRequestMessage request,
        bool includeAjaxHeader)
    {
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/91.0.4472.124 Safari/537.36");

        request.Headers.Referrer = new Uri("https://xboxunity.net/");
        request.Headers.Accept.ParseAdd(
            "application/json, text/javascript, */*; q=0.01");

        request.Headers.TryAddWithoutValidation(
            "Accept-Language",
            "en-US,en;q=0.5");

        if (includeAjaxHeader)
        {
            request.Headers.TryAddWithoutValidation(
                "X-Requested-With",
                "XMLHttpRequest");
        }
    }

    private static string GetSafeFileName(
        ContentDispositionHeaderValue? disposition,
        string fallback)
    {
        string? fileName = disposition?.FileNameStar;

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = disposition?.FileName;

        if (string.IsNullOrWhiteSpace(fileName))
            return Path.GetFileName(fallback);

        fileName = Path.GetFileName(fileName.Trim().Trim('"'));

        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or "..")
        {
            return Path.GetFileName(fallback);
        }

        return fileName;
    }

    private static string NormalizeId(string? value)
    {
        value = value?.Trim() ?? string.Empty;

        if (value.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return value.ToUpperInvariant();
    }

    private static bool IsHexId(string value)
    {
        return value.Length == 8 &&
            value.All(Uri.IsHexDigit);
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? string.Empty,
            JsonValueKind.Number => property.GetRawText(),
            _ => string.Empty
        };
    }

    private static int GetInt32(
        JsonElement element,
        string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt32(out int value))
        {
            return value;
        }

        return 0;
    }
}
