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

    private sealed record UpdateInfo(
        string FileName,
        string DownloadUrl);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        return client;
    }

    public async Task<DownloadResult> DownloadAsync(
        string titleId,
        string mediaId,
        string destinationFolder,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var updates = new List<UpdateInfo>();

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

        try
        {
            Report($"Searching XboxUnity for Title ID {titleId}...");

            using var searchTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            searchTimeout.CancelAfter(TimeSpan.FromSeconds(30));

            var searchUrl =
                $"{ResourcesUrl}/TitleUpdateInfo.php?titleid=" +
                Uri.EscapeDataString(titleId);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                searchUrl);

            AddBrowserHeaders(request, includeAjaxHeader: true);

            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                searchTimeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                Report(
                    $"XboxUnity search failed: HTTP {(int)response.StatusCode}.");

                return new(0, 0, 1, messages);
            }

            await using var responseStream =
                await response.Content.ReadAsStreamAsync(
                    searchTimeout.Token);

            using var json = await JsonDocument.ParseAsync(
                responseStream,
                cancellationToken: searchTimeout.Token);

            updates = ParseUpdates(
                json.RootElement,
                titleId,
                mediaId);

            Report($"Found {updates.Count} matching Title Update(s).");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Report("Title Update search was cancelled.");
            return new(updates.Count, 0, 1, messages);
        }
        catch (OperationCanceledException)
        {
            Report("XboxUnity search timed out.");
            return new(updates.Count, 0, 1, messages);
        }
        catch (HttpRequestException)
        {
            Report("Could not connect to XboxUnity to search for updates.");
            return new(updates.Count, 0, 1, messages);
        }
        catch (JsonException)
        {
            Report("XboxUnity returned an invalid JSON response.");
            return new(updates.Count, 0, 1, messages);
        }
        catch (IOException)
        {
            Report("Could not read the XboxUnity search response.");
            return new(updates.Count, 0, 1, messages);
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

                var actualFileName = GetSafeFileName(
                    response.Content.Headers.ContentDisposition,
                    update.FileName);

                var destinationPath = Path.Combine(
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
                Report(
                    $"Could not save {update.FileName}: {ex.Message}");

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
                        // A leftover temporary file must not hide
                        // the original download result.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Same handling for cleanup permission errors.
                    }
                }
            }
        }

        Report(
            $"Title Update summary: {updates.Count} found, " +
            $"{downloaded} downloaded, {failed} failed.");

        return new(updates.Count, downloaded, failed, messages);
    }

    private static List<UpdateInfo> ParseUpdates(
        JsonElement root,
        string titleId,
        string mediaId)
    {
        var result = new List<UpdateInfo>();

        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

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
                {
                    AddUpdates(result, items, titleId);
                }
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
        List<UpdateInfo> result,
        JsonElement items,
        string titleId)
    {
        if (items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            AddUpdate(result, item, titleId);
        }
    }

    private static void AddUpdate(
        List<UpdateInfo> result,
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

        result.Add(new UpdateInfo(
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
        {
            fileName = disposition?.FileName;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Path.GetFileName(fallback);
        }

        fileName = fileName.Trim().Trim('"');

        // Do not allow a server-supplied path to escape the destination.
        fileName = Path.GetFileName(fileName);

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
        {
            return string.Empty;
        }

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
