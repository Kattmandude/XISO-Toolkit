namespace XISO.Core.Metadata;

public sealed class Xex2SecurityInfo
{
    public uint ImageSize { get; init; }

    public uint LoadAddress { get; init; }

    public byte[] AesKey { get; init; } = [];
}
