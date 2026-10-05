namespace XISO.Core.Metadata;

public sealed class Xex2Reader
{
    public Xex2Header ReadHeader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 24)
            throw new InvalidDataException(
                "XEX2 header requires at least 24 bytes.");

        uint magic =
            ReadUInt32BigEndian(data, 0);

        if (magic != 0x58455832)
            throw new InvalidDataException(
                "Invalid XEX2 magic.");

        return new Xex2Header
        {
            ModuleFlags = ReadUInt32BigEndian(data, 4),
            PeDataOffset = ReadUInt32BigEndian(data, 8),
            Reserved = ReadUInt32BigEndian(data, 12),
            SecurityInfoOffset = ReadUInt32BigEndian(data, 16),
            OptionalHeaderCount = ReadUInt32BigEndian(data, 20)
        };
    }

    public List<Xex2OptionalHeader> ReadOptionalHeaders(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 24)
            throw new InvalidDataException(
                "XEX2 header requires at least 24 bytes.");

        uint magic =
            ReadUInt32BigEndian(data, 0);

        if (magic != 0x58455832)
            throw new InvalidDataException(
                "Invalid XEX2 magic.");

        uint optionalHeaderCount =
            ReadUInt32BigEndian(data, 20);

        const int fixedHeaderSize = 24;
        const int optionalHeaderSize = 8;

        long requiredSize =
            fixedHeaderSize +
            ((long)optionalHeaderCount * optionalHeaderSize);

        if (requiredSize > data.Length)
        {
            throw new InvalidDataException(
                $"XEX2 optional header table requires {requiredSize} bytes, " +
                $"but only {data.Length} bytes were provided.");
        }

        var headers =
            new List<Xex2OptionalHeader>((int)optionalHeaderCount);

        int offset = fixedHeaderSize;

        for (int i = 0; i < optionalHeaderCount; i++)
        {
            uint key =
                ReadUInt32BigEndian(data, offset);

            uint value =
                ReadUInt32BigEndian(data, offset + 4);

            headers.Add(
                new Xex2OptionalHeader
                {
                    Key = key,
                    Value = value
                });

            offset += optionalHeaderSize;
        }

        return headers;
    }
    public Xex2OptionalHeader? FindOptionalHeader(
        byte[] data,
        uint type,
        byte formatCode)
    {
        var headers = ReadOptionalHeaders(data);

        foreach (var header in headers)
        {
            if (header.Type == type &&
                header.FormatCode == formatCode)
            {
                return header;
            }
        }

        return null;
    }
    public Xex2ExecutionId ReadExecutionId(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        var header =
            FindOptionalHeader(
                xexData,
                0x000400,
                0x06);

        if (header is null)
        {
            throw new InvalidDataException(
                "XEX2 Execution ID optional header was not found.");
        }

        const int executionIdSize = 24;

        long end =
            (long)header.Value + executionIdSize;

        if (header.Value > int.MaxValue ||
            end > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Execution ID extends beyond the supplied XEX data.");
        }

        byte[] executionIdData =
            xexData.AsSpan(
                (int)header.Value,
                executionIdSize)
            .ToArray();

        return ReadExecutionIdData(executionIdData);
    }
    public Xex2ExecutionId ReadExecutionIdData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        const int executionIdSize = 24;

        if (data.Length < executionIdSize)
            throw new InvalidDataException(
                "XEX2 Execution ID requires at least 24 bytes.");

        return new Xex2ExecutionId
        {
            MediaId = ReadUInt32BigEndian(data, 0),
            Version = ReadUInt32BigEndian(data, 4),
            BaseVersion = ReadUInt32BigEndian(data, 8),
            TitleId = ReadUInt32BigEndian(data, 12),
            Platform = data[16],
            ExecutableType = data[17],
            DiscNumber = data[18],
            DiscCount = data[19],
            SaveGameId = ReadUInt32BigEndian(data, 20)
        };
    }
    private static uint ReadUInt32BigEndian(
        byte[] data,
        int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               data[offset + 3];
    }
}