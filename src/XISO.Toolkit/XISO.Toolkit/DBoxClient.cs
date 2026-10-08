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

        string encodedXmid =
            Uri.EscapeDataString(xmid.Trim());

        return await _httpClient.GetFromJsonAsync<DBoxDisc>(
            $"api/discs/xmid/{encodedXmid}",
            cancellationToken);
    }

    public async Task<string?> GetRawDiscByXmidAsync(
        string xmid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmid))
            return null;

        string encodedXmid =
            Uri.EscapeDataString(xmid.Trim());

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                $"api/discs/xmid/{encodedXmid}",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
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
