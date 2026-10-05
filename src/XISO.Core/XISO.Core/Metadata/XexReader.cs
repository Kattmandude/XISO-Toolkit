using System.Buffers.Binary;
using XISO.Core.Models;

namespace XISO.Core.Metadata;

public class XexReader
{
    private const uint XexMagic = 0x58455832; // XEX2

    public bool IsValidXex(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var stream = File.OpenRead(filePath);

        Span<byte> buffer = stackalloc byte[4];

        if (stream.Read(buffer) != 4)
            return false;

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(buffer);

        return magic == XexMagic;
    }

    public void ReadMetadata(string filePath, GameImageInfo info)
    {
        if (!IsValidXex(filePath))
            return;

        using var stream = File.OpenRead(filePath);

        Span<byte> header = stackalloc byte[24];

        stream.ReadExactly(header);

        uint optionalHeaderCount =
            BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);

        info.Version = $"XEX optional headers: {optionalHeaderCount}";
    }
}