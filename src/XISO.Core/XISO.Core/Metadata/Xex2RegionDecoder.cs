namespace XISO.Core.Metadata;

public static class Xex2RegionDecoder
{
    public static string Decode(uint regionMask)
    {
        if (regionMask == 0xFFFFFFFF)
            return "All Regions";

        if (regionMask == 0x00000100)
            return "Japan";

        if (regionMask == 0x00000200)
            return "China";

        if (regionMask == 0x00010000)
            return "Australia/New Zealand";

        var regions = new List<string>();

        if ((regionMask & 0x000000FF) != 0)
            regions.Add("NTSC-U");

        if ((regionMask & 0x0000FF00) != 0)
            regions.Add("NTSC-J");

        if ((regionMask & 0x00FF0000) != 0)
            regions.Add("PAL");

        if ((regionMask & 0xFF000000) != 0)
            regions.Add("Other");

        return regions.Count > 0
            ? string.Join(", ", regions)
            : $"Unknown (0x{regionMask:X8})";
    }
}
