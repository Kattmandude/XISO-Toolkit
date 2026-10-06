namespace XISO.OriginalXbox;

public sealed class XbeReader
{
    private readonly byte[] _data;

    public uint BaseAddress { get; }
    public uint CertificateAddress { get; }

    public XbeReader(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 0x104)
        {
            throw new InvalidDataException(
                "The XBE data is too small to contain a valid header.");
        }

        uint magic =
            ReadUInt32LittleEndian(data, 0x00);

        if (magic != 0x48454258)
        {
            throw new InvalidDataException(
                "The executable does not contain a valid XBE header.");
        }

        _data = data;

        BaseAddress =
            ReadUInt32LittleEndian(data, 0x104);

        CertificateAddress =
            ReadUInt32LittleEndian(data, 0x118);

        uint certificateOffset =
            CertificateAddress - BaseAddress;

        if (certificateOffset > int.MaxValue ||
            certificateOffset + 4 > data.Length)
        {
            throw new InvalidDataException(
                "The XBE certificate address is outside the executable.");
        }

        CertificateSize =
            ReadUInt32LittleEndian(
                data,
                (int)certificateOffset);
    }

    public uint CertificateSize { get; }

    public string SerialNumber
    {
        get
        {
            byte prefix1 = (byte)(TitleId >> 24);
            byte prefix2 = (byte)(TitleId >> 16);
            ushort number = (ushort)(TitleId & 0xFFFF);

            return
                $"{(char)prefix1}{(char)prefix2}-{number:000}";
        }
    }

    public string Xmid
    {
        get
        {
            string serial =
                SerialNumber.Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal);

            string buildVersion =
                (Version & 0xFF)
                    .ToString("X2");

            int buildNumber =
                Convert.ToInt32(
                    buildVersion,
                    16);

            string regionCode =
                GameRegion switch
                {
                    1 => "A",
                    2 => "J",
                    3 => "K",
                    4 => "E",
                    5 => "L",
                    7 => "W",
                    _ => string.Empty
                };

            if (string.IsNullOrEmpty(regionCode))
            {
                return string.Empty;
            }

            return
                $"{serial}{buildNumber:00}{regionCode}";
        }
    }

    public uint TitleId
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0x08));
        }
    }

    public IReadOnlyList<uint> AlternateTitleIds
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            var ids = new List<uint>(16);

            for (int i = 0; i < 16; i++)
            {
                ids.Add(
                    ReadUInt32LittleEndian(
                        _data,
                        checked((int)certificateOffset + 0x5C + (i * 4))));
            }

            return ids;
        }
    }

    public uint AllowedMedia
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0x04));
        }
    }

    public uint GameRatings
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0xA4));
        }
    }

    public string TitleName
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            int offset =
                checked((int)certificateOffset + 0x0C);

            int byteLength =
                40 * 2;

            if (offset + byteLength > _data.Length)
            {
                throw new InvalidDataException(
                    "The XBE title name is outside the executable.");
            }

            return
                System.Text.Encoding.Unicode
                    .GetString(_data, offset, byteLength)
                    .TrimEnd('\0');
        }
    }

    public uint GameRegion
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0xA0));
        }
    }

    public uint DiskNumber
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0xA8));
        }
    }

    public uint Version
    {
        get
        {
            uint certificateOffset =
                CertificateAddress - BaseAddress;

            return ReadUInt32LittleEndian(
                _data,
                checked((int)certificateOffset + 0xAC));
        }
    }

    public string Md5
    {
        get
        {
            byte[] hash =
                System.Security.Cryptography.MD5.HashData(_data);

            return Convert.ToHexString(hash);
        }
    }

    private static uint ReadUInt32LittleEndian(
        byte[] data,
        int offset)
    {
        return
            (uint)(
                data[offset] |
                (data[offset + 1] << 8) |
                (data[offset + 2] << 16) |
                (data[offset + 3] << 24));
    }
}
