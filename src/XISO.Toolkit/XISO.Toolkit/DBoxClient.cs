
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XISO.Toolkit;

public sealed class DBoxClient
{
    private readonly HttpClient _httpClient;

    public DBoxClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://dbox.tools/")
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "XISO-Toolkit/1.0");
    }

    public async Task<DBoxDisc?> GetDiscByXmidAsync(
        string xmid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmid))
            return null;

        string encoded = Uri.EscapeDataString(xmid.Trim());

        return await GetDiscAsync(
            $"api/discs/xmid/{encoded}",
            cancellationToken);
    }

    public async Task<DBoxDisc?> GetDiscByMediaIdAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        string encoded = Uri.EscapeDataString(mediaId.Trim());

        return await GetDiscAsync(
            $"api/discs/media_id/{encoded}",
            cancellationToken);
    }

    public async Task<string?> GetTitleNameByTitleIdAsync(
        string titleId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(titleId))
            return null;

        string normalizedTitleId = titleId.Trim();

        if (normalizedTitleId.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            normalizedTitleId = normalizedTitleId[2..];
        }

        string encoded = Uri.EscapeDataString(
            normalizedTitleId.ToUpperInvariant());

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                $"api/title_ids/{encoded}",
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using Stream content =
            await response.Content.ReadAsStreamAsync(
                cancellationToken);

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                content,
                cancellationToken: cancellationToken);

        if (TryGetStringProperty(
            document.RootElement,
            "name",
            out string? name))
        {
            return string.IsNullOrWhiteSpace(name)
                ? null
                : name.Trim();
        }

        return null;
    }

    public async Task<string?> GetRawDiscByXmidAsync(
        string xmid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmid))
            return null;

        string encoded = Uri.EscapeDataString(xmid.Trim());

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                $"api/discs/xmid/{encoded}",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
    }

    private async Task<DBoxDisc?> GetDiscAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                requestUri,
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DBoxDisc>(
            cancellationToken: cancellationToken);
    }

    private static bool TryGetStringProperty(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!property.Name.Equals(
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                value = property.Value.GetString();
                return true;
            }

            return false;
        }

        return false;
    }
}

public sealed class DBoxDisc
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("xmid")]
    public string? Xmid { get; set; }

    [JsonPropertyName("xemid")]
    public string? Xemid { get; set; }

    [JsonPropertyName("media_id_v1")]
    public string? MediaIdV1 { get; set; }

    [JsonPropertyName("media_id_v2")]
    public string? MediaIdV2 { get; set; }

    [JsonPropertyName("redump_id")]
    public int? RedumpId { get; set; }

    [JsonPropertyName("redump_name")]
    public string? RedumpName { get; set; }

    [JsonPropertyName("redump_datname")]
    public string? RedumpDatName { get; set; }

    [JsonPropertyName("redump_media")]
    public string? RedumpMedia { get; set; }

    [JsonPropertyName("redump_category")]
    public string? RedumpCategory { get; set; }

    [JsonPropertyName("redump_region")]
    public string? RedumpRegion { get; set; }

    [JsonPropertyName("redump_edition")]
    public string? RedumpEdition { get; set; }

    [JsonPropertyName("redump_languages")]
    public string? RedumpLanguages { get; set; }
}
