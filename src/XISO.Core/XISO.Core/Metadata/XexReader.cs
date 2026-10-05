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

        var entry = new byte[8];

        for (int i = 0; i < optionalHeaderCount; i++)
        {
            if (stream.Read(entry, 0, 8) != 8)
                break;

            uint headerValue =
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(entry[0..4]);

            uint value =
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(entry[4..8]);

            uint headerId = headerValue >> 8;

            if (headerId == 0x000405)
            {
                stream.Position = value;

                Span<byte> descriptor = stackalloc byte[8];

                if (stream.Read(descriptor) != 8)
                    return;

                uint executionIdOffset =
                    BinaryPrimitives.ReadUInt32BigEndian(descriptor[0..4]);

                stream.Position = executionIdOffset;

                byte[] executionId = new byte[24];

                if (stream.Read(executionId, 0, 24) == 24)
                {
                    uint mediaId =
                        BinaryPrimitives.ReadUInt32BigEndian(executionId.AsSpan(0, 4));

                    uint version =
                        BinaryPrimitives.ReadUInt32BigEndian(executionId.AsSpan(4, 4));

                    uint baseVersion =
                        BinaryPrimitives.ReadUInt32BigEndian(executionId.AsSpan(8, 4));

                    uint titleId =
                        BinaryPrimitives.ReadUInt32BigEndian(executionId.AsSpan(12, 4));

                    info.TitleId = titleId.ToString("X8");

                    info.Version =
                        $"Media:{mediaId:X8} Version:{version:X8} Base:{baseVersion:X8}";
                }
            }
        }
    }
}