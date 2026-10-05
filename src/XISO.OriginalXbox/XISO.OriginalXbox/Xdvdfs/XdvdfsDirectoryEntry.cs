namespace XISO.OriginalXbox.Xdvdfs;

public sealed class XdvdfsDirectoryEntry
{
    public ushort Left { get; init; }

    public ushort Right { get; init; }

    public uint StartSector { get; init; }

    public uint FileSize { get; init; }

    public byte Attributes { get; init; }

    public string Name { get; init; } = string.Empty;
}
