using XISO.Core.Detection;

namespace XISO.Core.Models;

public class GameImageInfo
{
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public XboxPlatform Platform { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Executable { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string TitleId { get; set; } = string.Empty;
    public string MediaId { get; set; } = string.Empty;

    // Xbox 360 XEX Execution ID metadata.
    public uint XexVersion { get; set; }
    public uint XexBaseVersion { get; set; }
    public byte XexPlatform { get; set; }
    public byte XexExecutableType { get; set; }
    public byte XexDiscNumber { get; set; }
    public byte XexDiscCount { get; set; }
    public uint XexSaveGameId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Xmid { get; set; } = string.Empty;
    public string XbeMd5 { get; set; } = string.Empty;
    public uint XbeSizeOfHeaders { get; set; }
    public uint XbeSizeOfImage { get; set; }
    public uint XbeTimeDate { get; set; }
    public uint XbeNumberOfSections { get; set; }
    public uint XbeInitFlags { get; set; }
    public uint XbeLibraryVersionCount { get; set; }
    public uint XbeLibraryVersionsAddress { get; set; }
    public IReadOnlyList<XbeLibraryVersion> XbeLibraryVersions { get; set; } =
        Array.Empty<XbeLibraryVersion>();

    public uint XbeRegionMask { get; set; }
    public uint AllowedMedia { get; set; }
    public string XbeRegion { get; set; } = string.Empty;
    public IReadOnlyList<uint> AlternateTitleIds { get; set; } = Array.Empty<uint>();
    public string ReleaseRegion { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public IReadOnlyList<CdxGameInfo> CdxGames { get; set; } =
        Array.Empty<CdxGameInfo>();
}

