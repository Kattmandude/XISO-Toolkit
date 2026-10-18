using System.Buffers.Binary;
using System.Text;

namespace XISO.Core.Metadata;

public sealed class XdbfTitleReader
{
    private const int XdbfHeaderSize = 24;
    private const int XdbfEntrySize = 18;
    private const int XdbfFreeEntrySize = 8;
    private const int XstrHeaderSize = 14;

    private const ushort StringTableNamespace = 3;
    private const ushort TitleStringId = 0x8000;
    private const ulong EnglishLanguageId = 1;

    private static readonly byte[] XdbfMagic =
        Encoding.ASCII.GetBytes("XDBF");

    private static readonly byte[] XstrMagic =
        Encoding.ASCII.GetBytes("XSTR");

    public string? ReadTitle(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 4)
            return null;

        if (data.AsSpan(0, 4).SequenceEqual(XdbfMagic))
            return ReadTitleFromXdbf(data);

        if (data.AsSpan(0, 4).SequenceEqual(XstrMagic))
            return ReadTitleFromXstr(data);

        return null;
    }

    private static string? ReadTitleFromXdbf(byte[] data)
    {
        if (data.Length < XdbfHeaderSize)
            return null;

        uint entryCapacity = ReadUInt32(data, 8);
        uint entryCount = ReadUInt32(data, 12);
        uint freeCapacity = ReadUInt32(data, 16);
        uint freeCount = ReadUInt32(data, 20);

        if (entryCount > entryCapacity ||
            freeCount > freeCapacity)
        {
            return null;
        }

        long entryTableEnd =
            XdbfHeaderSize + (long)entryCapacity * XdbfEntrySize;

        long dataStart =
            entryTableEnd + (long)freeCapacity * XdbfFreeEntrySize;

        if (entryTableEnd > data.Length ||
            dataStart > data.Length)
        {
            return null;
        }

        var stringTables = new List<(ulong LanguageId, byte[] Data)>();

        for (uint i = 0; i < entryCount; i++)
        {
            int entryOffset =
                checked(XdbfHeaderSize + (int)i * XdbfEntrySize);

            ushort entryNamespace =
                ReadUInt16(data, entryOffset);

            if (entryNamespace != StringTableNamespace)
                continue;

            ulong languageId = ReadUInt64(data, entryOffset + 2);
            uint relativeOffset = ReadUInt32(data, entryOffset + 10);
            uint length = ReadUInt32(data, entryOffset + 14);

            long absoluteOffset = dataStart + relativeOffset;

            if (length < XstrHeaderSize ||
                absoluteOffset < dataStart ||
                absoluteOffset + length > data.Length ||
                length > int.MaxValue)
            {
                continue;
            }

            byte[] table = data.AsSpan(
                (int)absoluteOffset,
                (int)length).ToArray();

            if (!table.AsSpan(0, 4).SequenceEqual(XstrMagic))
                continue;

            stringTables.Add((languageId, table));
        }

        foreach (var table in stringTables
            .OrderBy(t => t.LanguageId == EnglishLanguageId ? 0 : 1))
        {
            string? title = ReadTitleFromXstr(table.Data);

            if (!string.IsNullOrWhiteSpace(title))
                return title;
        }

        return null;
    }

    private static string? ReadTitleFromXstr(byte[] data)
    {
        if (data.Length < XstrHeaderSize ||
            !data.AsSpan(0, 4).SequenceEqual(XstrMagic))
        {
            return null;
        }

        uint declaredSize = ReadUInt32(data, 8);
        ushort stringCount = ReadUInt16(data, 12);

        long declaredEnd = 4L + declaredSize;

        if (declaredSize < XstrHeaderSize - 4 ||
            declaredEnd > data.Length)
        {
            return null;
        }

        int position = XstrHeaderSize;
        int end = (int)declaredEnd;

        for (int i = 0; i < stringCount; i++)
        {
            if (position + 4 > end)
                return null;

            ushort stringId = ReadUInt16(data, position);
            ushort stringLength = ReadUInt16(data, position + 2);

            position += 4;

            if (position + stringLength > end)
                return null;

            if (stringId == TitleStringId && stringLength > 0)
            {
                string title = Encoding.UTF8.GetString(
                    data, position, stringLength);

                title = title.Trim('\0', ' ', '\t', '\r', '\n');

                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }

            position += stringLength;
        }

        return null;
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static ulong ReadUInt64(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset, 8));
}