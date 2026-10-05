namespace XISO.OriginalXbox;

public sealed class Xex2Header
{
    public uint ModuleFlags { get; init; }

    public uint PeDataOffset { get; init; }

    public uint Reserved { get; init; }

    public uint SecurityInfoOffset { get; init; }

    public uint OptionalHeaderCount { get; init; }
}