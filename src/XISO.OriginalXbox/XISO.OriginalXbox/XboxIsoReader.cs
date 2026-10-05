using System.Text;
using XISO.Core.Detection;
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

    public XboxExecutableFormat DetectExecutableFormat()
    {
        var executable = FindDefaultExecutable();

        if (executable is null)
            return XboxExecutableFormat.Unknown;

        byte[] header = ReadFilePrefix(executable, 4);

        if (header.Length < 4)
            return XboxExecutableFormat.Unknown;

        if (header[0] == 0x58 &&
            header[1] == 0x45 &&
            header[2] == 0x58 &&
            header[3] == 0x32)
        {
            return XboxExecutableFormat.Xex2;
        }

        if (header[0] == 0x58 &&
            header[1] == 0x42 &&
            header[2] == 0x45 &&
            header[3] == 0x48)
        {
            return XboxExecutableFormat.Xbe;
        }

        return XboxExecutableFormat.Unknown;
    }
    public XboxPlatform DetectPlatform()
    {
        var executable = FindDefaultExecutable();

        if (executable is null)
            return XboxPlatform.Unknown;

        if (string.Equals(
            executable.Name,
            "default.xbe",
            StringComparison.OrdinalIgnoreCase))
        {
            return XboxPlatform.OriginalXbox;
        }

        if (string.Equals(
            executable.Name,
            "default.xex",
            StringComparison.OrdinalIgnoreCase))
        {
            return XboxPlatform.Xbox360;
        }

        return XboxPlatform.Unknown;
    }
    public XdvdfsDirectoryEntry? FindDefaultExecutable()
    {
        var xbe = FindEntry("default.xbe");

        if (xbe is not null)
            return xbe;

        return FindEntry("default.xex");
    }
    public byte[] ReadFilePrefix(
        XdvdfsDirectoryEntry entry,
        int byteCount)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (byteCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                "Byte count cannot be negative.");

        int count = Math.Min(byteCount, (int)entry.FileSize);

        long fileOffset =
            VolumeDescriptor.PartitionOffset +
            ((long)entry.StartSector * SectorSize);

        long fileEnd =
            fileOffset + count;

        if (fileOffset < 0 || fileEnd > _stream.Length)
        {
            throw new InvalidDataException(
                $"File '{entry.Name}' extends beyond the end of the ISO.");
        }

        _stream.Position = fileOffset;

        byte[] data = new byte[count];

        ReadExactly(data);

        return data;
    }
    public byte[] ReadFile(XdvdfsDirectoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        long fileOffset =
            VolumeDescriptor.PartitionOffset +
            ((long)entry.StartSector * SectorSize);

        long fileEnd =
            fileOffset + entry.FileSize;

        if (fileOffset < 0 || fileEnd > _stream.Length)
        {
            throw new InvalidDataException(
                $"File '{entry.Name}' extends beyond the end of the ISO.");
        }

        _stream.Position = fileOffset;

        byte[] data = new byte[entry.FileSize];

        ReadExactly(data);

        return data;
    }
    public IReadOnlyList<XdvdfsFileEntry> EnumerateFiles()
    {
        var files = new List<XdvdfsFileEntry>();

        void WalkDirectory(
            long directoryOffset,
            uint directorySize,
            string relativePath)
        {
            var visited = new HashSet<uint>();
            var pending = new Stack<uint>();

            pending.Push(0);

            while (pending.Count > 0)
            {
                uint dwordOffset = pending.Pop();

                if (!visited.Add(dwordOffset))
                    continue;

                long byteOffset =
                    (long)dwordOffset * 4;

                if (byteOffset >= directorySize)
                {
                    throw new InvalidDataException(
                        $"Directory entry DWORD offset {dwordOffset} " +
                        $"is outside the directory.");
                }

                long entryOffset =
                    directoryOffset +
                    byteOffset;

                _stream.Position = entryOffset;

                var entry =
                    ReadDirectoryEntryAtCurrentPosition();

                string entryPath =
                    string.IsNullOrEmpty(relativePath)
                        ? entry.Name
                        : Path.Combine(relativePath, entry.Name);

                if ((entry.Attributes & 0x10) != 0)
                {
                    long childDirectoryOffset =
                        VolumeDescriptor.PartitionOffset +
                        ((long)entry.StartSector * SectorSize);

                    WalkDirectory(
                        childDirectoryOffset,
                        entry.FileSize,
                        entryPath);
                }
                else
                {
                    files.Add(
                        new XdvdfsFileEntry
                        {
                            Entry = entry,
                            RelativePath = entryPath
                        });
                }

                if (entry.Right != 0)
                    pending.Push(entry.Right);

                if (entry.Left != 0)
                    pending.Push(entry.Left);
            }
        }

        long rootDirectoryOffset =
            VolumeDescriptor.PartitionOffset +
            ((long)VolumeDescriptor.RootDirectorySector * SectorSize);

        WalkDirectory(
            rootDirectoryOffset,
            VolumeDescriptor.RootDirectorySize,
            string.Empty);

        return files;
    }
    public XdvdfsDirectoryEntry? FindEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Entry name cannot be empty.",
                nameof(name));

        foreach (var entry in ReadRootDirectory())
        {
            if (string.Equals(
                entry.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
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
