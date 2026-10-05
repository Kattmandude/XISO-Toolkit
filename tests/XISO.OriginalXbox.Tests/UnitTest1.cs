using System.Text;
using XISO.OriginalXbox.Xdvdfs;

namespace XISO.OriginalXbox.Tests;

public class XboxIsoReaderTests
{
    [Fact]
    public void ReadsWorldOfOutlawsVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader = new XISO.OriginalXbox.XboxIsoReader(isoPath);

        Assert.Equal(0x0FDA0000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x000AD298U, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x00000800U, reader.VolumeDescriptor.RootDirectorySize);
    }

    [Fact]
    public void ReadsNinjaGaidenIIVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\Ninja Gaiden II (World) (En,Ja,Fr,De,Es,It,Zh,Ko,Pl,Ru).iso";

        using var reader = new XISO.OriginalXbox.XboxIsoReader(isoPath);

        Assert.Equal(0x0FDA0000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x000094ECU, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x00000800U, reader.VolumeDescriptor.RootDirectorySize);
    }

    [Fact]
    public void ReadsPandoraTomorrowVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\Tom Clancy's Splinter Cell - Pandora Tomorrow (USA, Europe) (En,Fr,De,Es,It).xiso.iso";

        using var reader = new XISO.OriginalXbox.XboxIsoReader(isoPath);

        Assert.Equal(0x00010000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x00000108U, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x0000016CU, reader.VolumeDescriptor.RootDirectorySize);
    }

    [Fact]
    public void ParsesKnownWorldOfOutlawsDirectoryEntry()
    {
        const long entryOffset = 0x666DC000;

        byte[] entryBytes =
        {
            0x06, 0x00,
            0x13, 0x00,
            0x94, 0x10, 0x0B, 0x00,
            0x81, 0x39, 0xF6, 0x06,
            0x80,
            0x09,
            0x64, 0x61, 0x74, 0x61, 0x31, 0x2E, 0x77, 0x61, 0x64
        };

        ushort left =
            BitConverter.ToUInt16(entryBytes, 0);

        ushort right =
            BitConverter.ToUInt16(entryBytes, 2);

        uint startSector =
            BitConverter.ToUInt32(entryBytes, 4);

        uint fileSize =
            BitConverter.ToUInt32(entryBytes, 8);

        byte attributes = entryBytes[12];

        byte nameLength = entryBytes[13];

        string name =
            Encoding.ASCII.GetString(
                entryBytes,
                14,
                nameLength);

        var entry = new XdvdfsDirectoryEntry
        {
            Left = left,
            Right = right,
            StartSector = startSector,
            FileSize = fileSize,
            Attributes = attributes,
            Name = name
        };

        Assert.Equal((ushort)6, entry.Left);
        Assert.Equal((ushort)19, entry.Right);
        Assert.Equal(0x000B1094U, entry.StartSector);
        Assert.Equal(116799873U, entry.FileSize);
        Assert.Equal((byte)0x80, entry.Attributes);
        Assert.Equal("data1.wad", entry.Name);

        Console.WriteLine($"Entry offset: 0x{entryOffset:X8}");
        Console.WriteLine($"Name: {entry.Name}");
        Console.WriteLine($"Start sector: 0x{entry.StartSector:X8}");
        Console.WriteLine($"Size: {entry.FileSize}");
    }

    [Fact]
    public void ReadsWorldOfOutlawsRootDirectoryEntryFromIso()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var entry = reader.ReadDirectoryEntry(0);

        Assert.Equal((ushort)6, entry.Left);
        Assert.Equal((ushort)19, entry.Right);
        Assert.Equal(0x000B1094U, entry.StartSector);
        Assert.Equal(116799873U, entry.FileSize);
        Assert.Equal((byte)0x80, entry.Attributes);
        Assert.Equal("data1.wad", entry.Name);

        Console.WriteLine($"Partition offset: 0x{reader.VolumeDescriptor.PartitionOffset:X8}");
        Console.WriteLine($"Root directory sector: 0x{reader.VolumeDescriptor.RootDirectorySector:X8}");
        Console.WriteLine($"Name: {entry.Name}");
        Console.WriteLine($"Left: {entry.Left}");
        Console.WriteLine($"Right: {entry.Right}");
        Console.WriteLine($"Start sector: 0x{entry.StartSector:X8}");
        Console.WriteLine($"Size: {entry.FileSize}");
    }

    [Fact]
    public void ReadsWorldOfOutlawsRootDirectory()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var entries = reader.ReadRootDirectory();

        Assert.NotEmpty(entries);

        Assert.Contains(
            entries,
            entry => entry.Name == "data1.wad");

        Assert.Contains(
            entries,
            entry => entry.Name == "data.wad");

        Assert.Contains(
            entries,
            entry => entry.Name == "nxeart");

        Assert.Contains(
            entries,
            entry => entry.Name == "default.xex");

        var data1 = entries.Single(
            entry => entry.Name == "data1.wad");

        Assert.Equal((ushort)6, data1.Left);
        Assert.Equal((ushort)19, data1.Right);
        Assert.Equal(0x000B1094U, data1.StartSector);
        Assert.Equal(116799873U, data1.FileSize);
        Assert.Equal((byte)0x80, data1.Attributes);

        Console.WriteLine($"Root entries: {entries.Count}");

        foreach (var entry in entries)
        {
            Console.WriteLine(
                $"{entry.Name} | " +
                $"Left={entry.Left} | " +
                $"Right={entry.Right} | " +
                $"Sector=0x{entry.StartSector:X8} | " +
                $"Size={entry.FileSize}");
        }
    }

}
