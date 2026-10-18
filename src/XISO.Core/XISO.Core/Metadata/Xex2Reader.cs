
namespace XISO.Core.Metadata;

public sealed class Xex2Reader
{
    public Xex2Header ReadHeader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 24)
            throw new InvalidDataException(
                "XEX2 header requires at least 24 bytes.");

        uint magic = ReadUInt32BigEndian(data, 0);

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

        uint magic = ReadUInt32BigEndian(data, 0);

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
            uint key = ReadUInt32BigEndian(data, offset);
            uint value = ReadUInt32BigEndian(data, offset + 4);

            headers.Add(new Xex2OptionalHeader
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

    /// <summary>
    /// Reads the 16-byte Media ID from the XEX Security Info structure.
    /// This is distinct from the four-byte MediaId field in the
    /// Execution ID optional header.
    /// </summary>
    public byte[] ReadMediaId(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        var header = ReadHeader(xexData);

        if (header.SecurityInfoOffset > int.MaxValue)
        {
            throw new InvalidDataException(
                "XEX2 Security Info offset is too large.");
        }

        // Media ID offset relative to the Security Info structure.
        const int mediaIdOffset = 0x140;
        const int mediaIdSize = 16;

        int securityOffset = (int)header.SecurityInfoOffset;
        long absoluteOffset = (long)securityOffset + mediaIdOffset;

        if (absoluteOffset + mediaIdSize > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Media ID extends beyond the supplied XEX data.");
        }

        return xexData.AsSpan(
            (int)absoluteOffset,
            mediaIdSize).ToArray();
    }

    public Xex2SecurityInfo ReadSecurityInfo(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        var header = ReadHeader(xexData);

        if (header.SecurityInfoOffset > int.MaxValue)
        {
            throw new InvalidDataException(
                "XEX2 Security Info offset is too large.");
        }

        int securityOffset = (int)header.SecurityInfoOffset;

        // Security Info header + image size + 256-byte RSA signature.
        const int minimumSecurityInfoSize = 4 + 4 + 256;

        if (securityOffset < 0 ||
            (long)securityOffset + minimumSecurityInfoSize > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Security Info extends beyond the supplied XEX data.");
        }

        uint imageSize =
            ReadUInt32BigEndian(xexData, securityOffset + 4);

        // Image Info begins after the 8-byte security header
        // and 256-byte signature.
        int imageInfoOffset = securityOffset + 8 + 256;

        // Load address is at Image Info + 8.
        uint loadAddress =
            ReadUInt32BigEndian(xexData, imageInfoOffset + 8);

        // AES key is at Image Info + 72.
        const int aesKeyOffset = 72;
        const int aesKeySize = 16;

        if ((long)imageInfoOffset + aesKeyOffset + aesKeySize >
            xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 AES key extends beyond the supplied XEX data.");
        }

        byte[] aesKey = xexData.AsSpan(
            imageInfoOffset + aesKeyOffset,
            aesKeySize).ToArray();

        // Game region is at offset 0x178 from the beginning
        // of the XEX2 Security Info structure.
        const int regionOffset = 0x178;

        if ((long)securityOffset + regionOffset + 4 > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 region field extends beyond the supplied XEX data.");
        }

        uint gameRegion = ReadUInt32BigEndian(
            xexData,
            securityOffset + regionOffset);

        return new Xex2SecurityInfo
        {
            ImageSize = imageSize,
            LoadAddress = loadAddress,
            AesKey = aesKey,
            GameRegion = gameRegion
        };
    }

    public Xex2ExecutionId ReadExecutionId(byte[] xexData)
    {
        ArgumentNullException.ThrowIfNull(xexData);

        var header = FindOptionalHeader(
            xexData,
            0x000400,
            0x06);

        if (header is null)
        {
            throw new InvalidDataException(
                "XEX2 Execution ID optional header was not found.");
        }

        const int executionIdSize = 24;

        long end = (long)header.Value + executionIdSize;

        if (header.Value > int.MaxValue ||
            end > xexData.Length)
        {
            throw new InvalidDataException(
                "XEX2 Execution ID extends beyond the supplied XEX data.");
        }

        byte[] executionIdData = xexData.AsSpan(
            (int)header.Value,
            executionIdSize).ToArray();

        return ReadExecutionIdData(executionIdData);
    }

    public Xex2ExecutionId ReadExecutionIdData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        const int executionIdSize = 24;

        if (data.Length < executionIdSize)
        {
            throw new InvalidDataException(
                "XEX2 Execution ID requires at least 24 bytes.");
        }

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
