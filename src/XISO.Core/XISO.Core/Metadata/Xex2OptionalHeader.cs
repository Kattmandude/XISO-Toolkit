namespace XISO.Core.Metadata;

public sealed class Xex2OptionalHeader
{
    public uint Key { get; init; }

    public uint Value { get; init; }

    public uint Type =>
        Key >> 8;

    public byte FormatCode =>
        (byte)(Key & 0xFF);
}