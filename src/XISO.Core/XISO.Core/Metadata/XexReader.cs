
using System.Buffers.Binary;
using XISO.Core.Models;

namespace XISO.Core.Metadata;

public class XexReader
{
    private const uint XexMagic = 0x58455832; // XEX2
    private const int XexHeaderSize = 24;
    private const int OptionalHeaderSize = 8;

    public bool IsValidXex(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var stream = File.OpenRead(filePath);
        return IsValidXex(stream);
    }

    public bool IsValidXex(ReadOnlySpan<byte> data)
    {
        return data.Length >= 4 &&
            BinaryPrimitives.ReadUInt32BigEndian(data[..4]) == XexMagic;
    }

    private static bool IsValidXex(Stream stream)
    {
        if (stream.Length < 4)
            return false;

        Span<byte> buffer = stackalloc byte[4];
        stream.Position = 0;

        return stream.Read(buffer) == 4 &&
            BinaryPrimitives.ReadUInt32BigEndian(buffer) == XexMagic;
    }

    public void ReadMetadata(string filePath, GameImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (!File.Exists(filePath))
            return;

        using var stream = File.OpenRead(filePath);
        ReadMetadata(stream, info);
    }

    /// <summary>
    /// Reads XEX metadata directly from bytes, without creating a file.
    /// </summary>
    public void ReadMetadata(byte[] data, GameImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(info);

        using var stream = new MemoryStream(data, writable: false);
        ReadMetadata(stream, info);
    }

    private void ReadMetadata(Stream stream, GameImageInfo info)
    {
        if (!stream.CanRead || !stream.CanSeek ||
            stream.Length < XexHeaderSize ||
            !IsValidXex(stream))
        {
            return;
        }

        stream.Position = 0;

        byte[] header = new byte[XexHeaderSize];
        stream.ReadExactly(header);

        uint optionalHeaderCount =
            BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));

        // Each optional-header entry occupies eight bytes.
        long maximumEntries =
            (stream.Length - XexHeaderSize) / OptionalHeaderSize;

        if (optionalHeaderCount > maximumEntries)
        {
            throw new InvalidDataException(
                "XEX optional-header table extends beyond the file.");
        }

        info.Version = $"XEX optional headers: {optionalHeaderCount}";

        byte[] entry = new byte[OptionalHeaderSize];

        for (uint i = 0; i < optionalHeaderCount; i++)
        {
            stream.ReadExactly(entry);

            uint key =
                BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(0, 4));

            uint value =
                BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(4, 4));

            // Execution ID optional-header key.
            if (key != 0x00040006)
                continue;

            // The value is the offset of the execution-info structure.
            if (value > stream.Length - 24)
                continue;

            stream.Position = value;

            byte[] executionId = new byte[24];
            stream.ReadExactly(executionId);

            uint mediaId =
                BinaryPrimitives.ReadUInt32BigEndian(
                    executionId.AsSpan(0, 4));

            uint version =
                BinaryPrimitives.ReadUInt32BigEndian(
                    executionId.AsSpan(4, 4));

            uint baseVersion =
                BinaryPrimitives.ReadUInt32BigEndian(
                    executionId.AsSpan(8, 4));

            uint titleId =
                BinaryPrimitives.ReadUInt32BigEndian(
                    executionId.AsSpan(12, 4));

            info.TitleId = titleId.ToString("X8");

            info.Version =
                $"Media:{mediaId:X8} " +
                $"Version:{version:X8} " +
                $"Base:{baseVersion:X8}";

            break;
        }
    }
}