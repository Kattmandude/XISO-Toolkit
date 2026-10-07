namespace XISO.Core.Models;

public class XbeLibraryVersion
{
    public string Name { get; set; } = string.Empty;

    public ushort MajorVersion { get; set; }

    public ushort MinorVersion { get; set; }

    public ushort BuildVersion { get; set; }

    public ushort Flags { get; set; }
}
