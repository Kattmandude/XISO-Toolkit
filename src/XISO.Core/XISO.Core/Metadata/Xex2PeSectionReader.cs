using System.Buffers.Binary;
using System.Text;

namespace XISO.Core.Metadata;

public static class Xex2PeSectionReader
{
    private const int SectionHeaderSize = 40;

    public static byte[]? FindTitleResource(
        byte[] peImage,
        string titleId)
    {
        ArgumentNullException.ThrowIfNull(peImage);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleId);

        if (peImage.Length < 64 ||
            peImage[0] != (byte)'M' ||
            peImage[1] != (byte)'Z')
        {
            return FindXdbf(peImage);
        }

        int peOffset = BinaryPrimitives.ReadInt32LittleEndian(
            peImage.AsSpan(0x3C, 4));

        if (peOffset < 0 ||
            (long)peOffset + 24 > peImage.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(
                peImage.AsSpan(peOffset, 4)) != 0x00004550)
        {
            return FindXdbf(peImage);
        }

        int headerOffset = peOffset + 4;

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(
            peImage.AsSpan(headerOffset + 2, 2));

        ushort optionalHeaderSize =
            BinaryPrimitives.ReadUInt16LittleEndian(
                peImage.AsSpan(headerOffset + 16, 2));

        long sectionTable = (long)headerOffset + 20 + optionalHeaderSize;
        long sectionTableEnd =
            sectionTable + (long)sectionCount * SectionHeaderSize;

        if (sectionTableEnd > peImage.Length)
            return FindXdbf(peImage);

        for (int i = 0; i < sectionCount; i++)
        {
            int offset = (int)sectionTable + i * SectionHeaderSize;

            string name = Encoding.ASCII.GetString(
                peImage, offset, 8).TrimEnd('\0');

            if (!string.Equals(
                name, titleId, StringComparison.OrdinalIgnoreCase))
                continue;

            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(
                peImage.AsSpan(offset + 8, 4));

            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(
                peImage.AsSpan(offset + 12, 4));

            byte[]? section = ReadRange(
                peImage, virtualAddress, virtualSize);

            if (HasXdbfMagic(section))
                return section;

            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(
                peImage.AsSpan(offset + 16, 4));

            uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(
                peImage.AsSpan(offset + 20, 4));

            section = ReadRange(peImage, rawPointer, rawSize);

            if (HasXdbfMagic(section))
                return section;

            break;
        }

        return FindXdbf(peImage);
    }

    private static byte[]? ReadRange(byte[] data, uint offset, uint size)
    {
        if (offset > int.MaxValue ||
            size < 4 ||
            size > int.MaxValue ||
            (long)offset + size > data.Length)
            return null;

        return data.AsSpan((int)offset, (int)size).ToArray();
    }

    private static byte[]? FindXdbf(byte[] data)
    {
        for (int i = 0; i <= data.Length - 4; i++)
        {
            if (data[i] == (byte)'X' &&
                data[i + 1] == (byte)'D' &&
                data[i + 2] == (byte)'B' &&
                data[i + 3] == (byte)'F')
                return data.AsSpan(i).ToArray();
        }

        return null;
    }

    private static bool HasXdbfMagic(byte[]? data) =>
        data is { Length: >= 4 } &&
        data[0] == (byte)'X' &&
        data[1] == (byte)'D' &&
        data[2] == (byte)'B' &&
        data[3] == (byte)'F';
}
