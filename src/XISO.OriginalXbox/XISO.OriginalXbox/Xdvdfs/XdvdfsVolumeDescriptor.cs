namespace XISO.OriginalXbox.Xdvdfs;

public sealed class XdvdfsVolumeDescriptor
{
    public long Offset { get; init; }

    public long PartitionOffset { get; init; }

    public uint RootDirectorySector { get; init; }

    public uint RootDirectorySize { get; init; }
}
