using System.Text;

namespace XISO.Core.Metadata;

public sealed class Xex2ResourceInfo
{
    public string Name { get; init; } = string.Empty;

    public uint Address { get; init; }

    public uint Size { get; init; }
}

public sealed class Xex2ResourceInfoReader
{
    private const uint ResourceInfoKey = 0x000002FF;
    private const int MainHeaderSize = 24;
    private const int OptionalHeaderSize = 8;
    private const int ResourceEntrySize = 16;

    public List<Xex2ResourceInfo> ReadResources(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        if (xexData.Length < MainHeaderSize)
            throw new InvalidDataException("XEX2 header is incomplete.");

        uint magic = ReadUInt32BigEndian(xexData, 0);

        if (magic != 0x58455832)
            throw new InvalidDataException("Invalid XEX2 magic.");

        uint headerCount = ReadUInt32BigEndian(xexData, 20);

        long tableEnd =
            MainHeaderSize + (long)headerCount * OptionalHeaderSize;

        if (tableEnd > xexData.Length)
            throw new InvalidDataException(
                "XEX2 optional header table extends beyond the file.");

        uint resourceInfoOffset = 0;
        bool found = false;

        for (int i = 0; i < headerCount; i++)
        {
            int offset = MainHeaderSize + i * OptionalHeaderSize;

            uint key = ReadUInt32BigEndian(xexData, offset);
            uint value = ReadUInt32BigEndian(xexData, offset + 4);

            if (key != ResourceInfoKey)
                continue;

            resourceInfoOffset = value;
            found = true;
            break;
        }

        if (!found)
            return [];

        if (resourceInfoOffset > int.MaxValue ||
            (long)resourceInfoOffset + 4 > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Resource Info header points outside the file.");
        }

        int infoOffset = (int)resourceInfoOffset;
        uint infoSize = ReadUInt32BigEndian(xexData, infoOffset);

        if (infoSize < 4 ||
            (long)infoOffset + infoSize > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Resource Info data has an invalid size.");
        }

        uint entriesSize = infoSize - 4;

        if (entriesSize % ResourceEntrySize != 0)
            throw new InvalidDataException(
                "XEX2 Resource Info contains an incomplete resource record.");

        int resourceCount = (int)(entriesSize / ResourceEntrySize);
        var resources = new List<Xex2ResourceInfo>(resourceCount);

        for (int i = 0; i < resourceCount; i++)
        {
            int entryOffset =
                infoOffset + 4 + i * ResourceEntrySize;

            string name = ReadResourceName(xexData, entryOffset);

            uint address =
                ReadUInt32BigEndian(xexData, entryOffset + 8);

            uint size =
                ReadUInt32BigEndian(xexData, entryOffset + 12);

            resources.Add(new Xex2ResourceInfo
            {
                Name = name,
                Address = address,
                Size = size
            });
        }

        return resources;
    }

    private static string ReadResourceName(byte[] data, int offset)
    {
        int length = 0;

        while (length < 8 && data[offset + length] != 0)
            length++;

        return Encoding.ASCII.GetString(data, offset, length);
    }

    private static uint ReadUInt32BigEndian(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }
}
