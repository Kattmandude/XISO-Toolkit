namespace XISO.Core.Metadata;

public sealed class Xex2ExecutionId
{
    public uint MediaId { get; init; }

    public uint Version { get; init; }

    public uint BaseVersion { get; init; }

    public uint TitleId { get; init; }

    public byte Platform { get; init; }

    public byte ExecutableType { get; init; }

    public byte DiscNumber { get; init; }

    public byte DiscCount { get; init; }

    public uint SaveGameId { get; init; }
}