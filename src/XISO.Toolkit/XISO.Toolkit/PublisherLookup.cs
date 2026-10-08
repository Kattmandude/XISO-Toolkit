namespace XISO.Toolkit;

public static class PublisherLookup
{
    private static readonly Dictionary<string, string> Publishers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AC"] = "Acclaim Entertainment",
            ["AV"] = "Activision",
            ["AT"] = "Atlus",
            ["BA"] = "Bandai",
            ["CC"] = "Capcom",
            ["CM"] = "Codemasters",
            ["EA"] = "Electronic Arts",
            ["ES"] = "Eidos Interactive",
            ["FI"] = "Fox Interactive",
            ["GF"] = "Gameloft",
            ["HU"] = "Hudson Soft",
            ["IG"] = "Infogrames",
            ["IP"] = "Interplay",
            ["KN"] = "Konami",
            ["LA"] = "LucasArts",
            ["MS"] = "Microsoft",
            ["MW"] = "Midway",
            ["NM"] = "Namco",
            ["NL"] = "NovaLogic",
            ["SE"] = "Sega",
            ["SN"] = "SNK",
            ["SQ"] = "Square Enix",
            ["TT"] = "Take-Two Interactive",
            ["US"] = "Ubisoft",
            ["VV"] = "Vicarious Visions",
            ["VU"] = "Vivendi Universal",
            ["WR"] = "Warner Bros.",
            ["XI"] = "Xicat Interactive"
        };

    public static string? GetPublisherId(string? xmid)
    {
        if (string.IsNullOrWhiteSpace(xmid) || xmid.Length < 2)
            return null;

        return xmid[..2].ToUpperInvariant();
    }

    public static string? GetGameId(string? xmid)
    {
        if (string.IsNullOrWhiteSpace(xmid) || xmid.Length < 5)
            return null;

        return xmid.Substring(2, 3).ToUpperInvariant();
    }

    public static string? GetSkuId(string? xmid)
    {
        if (string.IsNullOrWhiteSpace(xmid) || xmid.Length < 7)
            return null;

        return xmid.Substring(5, 2).ToUpperInvariant();
    }

    public static string? GetRegionId(string? xmid)
    {
        if (string.IsNullOrWhiteSpace(xmid) || xmid.Length < 8)
            return null;

        return xmid.Substring(7, 1).ToUpperInvariant();
    }
    public static string? GetPublisherName(string? xmid)
    {
        string? publisherId = GetPublisherId(xmid);

        if (publisherId == null)
            return null;

        if (Publishers.TryGetValue(publisherId, out string? name))
            return name;

        return null;
    }
}

