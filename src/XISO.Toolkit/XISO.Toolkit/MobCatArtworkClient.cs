using System.Net;

namespace XISO.Toolkit;

public sealed class MobCatArtworkClient
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly string ArtworkDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "XISO.Toolkit",
            "Artwork",
            "OriginalXbox");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "XISO-Toolkit/1.0");

        return client;
    }

    public async Task<string?> DownloadThumbnailAsync(
        string? xmid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmid))
        {
            return null;
        }

        string normalizedXmid = xmid.Trim().ToUpperInvariant();

        if (normalizedXmid.Length != 8 ||
            normalizedXmid.Any(
                character => !char.IsAsciiLetterOrDigit(character)))
        {
            return null;
        }

        string prefix = normalizedXmid[..2];

        string artworkUrl =
            "https://raw.githubusercontent.com/" +
            "MobCat/MobCats-original-xbox-game-list/main/" +
            $"thumbnail/{prefix}/{normalizedXmid}.jpg";

        Directory.CreateDirectory(ArtworkDirectory);

        string artworkPath = Path.Combine(
            ArtworkDirectory,
            $"{normalizedXmid}.jpg");

        if (File.Exists(artworkPath))
        {
            return artworkPath;
        }

        using HttpResponseMessage response =
            await HttpClient.GetAsync(
                artworkUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        byte[] imageBytes =
            await response.Content.ReadAsByteArrayAsync(
                cancellationToken);

        if (imageBytes.Length == 0)
        {
            return null;
        }

        string temporaryPath =
            artworkPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                imageBytes,
                cancellationToken);

            File.Move(
                temporaryPath,
                artworkPath,
                overwrite: true);

            return artworkPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
