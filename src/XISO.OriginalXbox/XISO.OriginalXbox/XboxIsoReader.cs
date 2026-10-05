using System.Text;
using XISO.OriginalXbox.Xdvdfs;

namespace XISO.OriginalXbox;

public sealed class XboxIsoReader : IDisposable
{
    private const int SectorSize = 2048;
    private const long DescriptorOffsetFromPartition = 0x10000;
    private const long StandardGamePartitionOffset = 0x0FD90000;
    private const string VolumeSignature = "MICROSOFT*XBOX*MEDIA";

    private readonly FileStream _stream;

    public string IsoPath { get; }

    public XdvdfsVolumeDescriptor VolumeDescriptor { get; }

    public XboxIsoReader(string isoPath)
    {
        if (string.IsNullOrWhiteSpace(isoPath))
            throw new ArgumentException("ISO path cannot be empty.", nameof(isoPath));

        if (!File.Exists(isoPath))
            throw new FileNotFoundException("ISO file was not found.", isoPath);

        IsoPath = Path.GetFullPath(isoPath);

        _stream = new FileStream(
            IsoPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        VolumeDescriptor = ReadVolumeDescriptor();
    }

    private XdvdfsVolumeDescriptor ReadVolumeDescriptor()
    {
        long partitionOffset = FindGamePartition();

        long descriptorOffset =
            partitionOffset + DescriptorOffsetFromPartition;

        _stream.Position = descriptorOffset;

        byte[] signatureBytes = new byte[VolumeSignature.Length];

        ReadExactly(signatureBytes);

        string signature = Encoding.ASCII.GetString(signatureBytes);

        if (signature != VolumeSignature)
        {
            throw new InvalidDataException(
                $"Invalid XDVDFS volume signature at 0x{descriptorOffset:X8}: '{signature}'.");
        }

        _stream.Position = descriptorOffset + 0x14;

        byte[] descriptorData = new byte[8];
        ReadExactly(descriptorData);

        uint rootDirectorySector =
            BitConverter.ToUInt32(descriptorData, 0);

        uint rootDirectorySize =
            BitConverter.ToUInt32(descriptorData, 4);

        return new XdvdfsVolumeDescriptor
        {
            Offset = descriptorOffset,
            PartitionOffset = partitionOffset,
            RootDirectorySector = rootDirectorySector,
            RootDirectorySize = rootDirectorySize
        };
    }

    public XdvdfsDirectoryEntry ReadDirectoryEntry(uint dwordOffset)
    {
        long directoryOffset =
            VolumeDescriptor.PartitionOffset +
            ((long)VolumeDescriptor.RootDirectorySector * SectorSize);

        long entryOffset =
            directoryOffset +
            ((long)dwordOffset * 4);

        _stream.Position = entryOffset;

        return ReadDirectoryEntryAtCurrentPosition();
    }

    public IReadOnlyList<XdvdfsDirectoryEntry> ReadRootDirectory()
    {
        var entries = new List<XdvdfsDirectoryEntry>();
        var visited = new HashSet<uint>();
        var pending = new Stack<uint>();

        // The root directory entry is always at DWORD offset 0.
        pending.Push(0);

        while (pending.Count > 0)
        {
            uint dwordOffset = pending.Pop();

            // Prevent malformed/cyclic directory trees from looping forever.
            if (!visited.Add(dwordOffset))
                continue;

            long byteOffset =
                (long)dwordOffset * 4;

            if (byteOffset >= VolumeDescriptor.RootDirectorySize)
            {
                throw new InvalidDataException(
                    $"Directory entry DWORD offset {dwordOffset} " +
                    $"is outside the root directory.");
            }

            var entry = ReadDirectoryEntry(dwordOffset);

            entries.Add(entry);

            // XDVDFS directory pointers are DWORD offsets.
            // A value of zero means there is no child pointer.
            if (entry.Right != 0)
                pending.Push(entry.Right);

            if (entry.Left != 0)
                pending.Push(entry.Left);
        }

        return entries;
    }

    private XdvdfsDirectoryEntry ReadDirectoryEntryAtCurrentPosition()
    {
        byte[] header = new byte[14];
        ReadExactly(header);

        ushort left =
            BitConverter.ToUInt16(header, 0);

        ushort right =
            BitConverter.ToUInt16(header, 2);

        uint startSector =
            BitConverter.ToUInt32(header, 4);

        uint fileSize =
            BitConverter.ToUInt32(header, 8);

        byte attributes = header[12];
        byte nameLength = header[13];

        byte[] nameBytes = new byte[nameLength];
        ReadExactly(nameBytes);

        string name =
            Encoding.ASCII.GetString(nameBytes);

        return new XdvdfsDirectoryEntry
        {
            Left = left,
            Right = right,
            StartSector = startSector,
            FileSize = fileSize,
            Attributes = attributes,
            Name = name
        };
    }

    private static int AlignToDword(int value)
    {
        return (value + 3) & ~3;
    }

    private static bool IsPadding(byte[] data, int offset)
    {
        return data[offset] == 0xFF ||
               data[offset] == 0x00;
    }

    private long FindGamePartition()
    {
        if (HasVolumeSignature(DescriptorOffsetFromPartition))
        {
            return 0;
        }

        if (HasVolumeSignature(
                StandardGamePartitionOffset + DescriptorOffsetFromPartition))
        {
            return StandardGamePartitionOffset;
        }

        throw new InvalidDataException(
            "Could not locate an XDVDFS game partition in the ISO.");
    }

    private bool HasVolumeSignature(long offset)
    {
        if (offset < 0 || offset + VolumeSignature.Length > _stream.Length)
            return false;

        _stream.Position = offset;

        byte[] signatureBytes = new byte[VolumeSignature.Length];

        ReadExactly(signatureBytes);

        return Encoding.ASCII.GetString(signatureBytes) == VolumeSignature;
    }

    private void ReadExactly(byte[] buffer)
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int bytesRead = _stream.Read(
                buffer,
                totalRead,
                buffer.Length - totalRead);

            if (bytesRead == 0)
                throw new EndOfStreamException(
                    "Unexpected end of ISO while reading XDVDFS data.");

            totalRead += bytesRead;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
