namespace XISO.OriginalXbox.Xdvdfs;

public sealed class XdvdfsFileEntry
{
    public XdvdfsDirectoryEntry Entry { get; init; } = null!;

    public string RelativePath { get; init; } = string.Empty;
}