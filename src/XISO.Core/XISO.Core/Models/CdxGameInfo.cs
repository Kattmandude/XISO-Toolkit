namespace XISO.Core.Models;

public class CdxGameInfo
{
    public string DisplayName { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string TitleId { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Xmid { get; set; } = string.Empty;

    public string XbeMd5 { get; set; } = string.Empty;

    public uint XbeRegionMask { get; set; }

    public string XbeRegion { get; set; } = string.Empty;

    public string ReleaseRegion { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}
