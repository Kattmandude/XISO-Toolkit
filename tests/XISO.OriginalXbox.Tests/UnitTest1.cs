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


    [Fact]
    public void FindsWorldOfOutlawsDefaultExecutable()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var entry = reader.FindDefaultExecutable();

        Assert.NotNull(entry);

        Assert.Equal("default.xex", entry.Name);
        Assert.Equal(0x000AFCDEU, entry.StartSector);
        Assert.Equal(10334208U, entry.FileSize);
        Assert.Equal((byte)0x80, entry.Attributes);

        Console.WriteLine($"Executable: {entry.Name}");
    }

    [Fact]
    public void ReadsWorldOfOutlawsDefaultXex()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var entry = reader.FindEntry("default.xex");

        Assert.NotNull(entry);

        var data = reader.ReadFile(entry);

        Assert.Equal(10334208, data.Length);

        Console.WriteLine($"Read {data.Length} bytes from {entry.Name}");
        Console.WriteLine(
            $"First 16 bytes: {Convert.ToHexString(data.AsSpan(0, 16))}");
    }

    [Fact]
    public void DetectsWorldOfOutlawsAsXbox360()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var platform = reader.DetectPlatform();

        Assert.Equal(
            XISO.Core.Detection.XboxPlatform.Xbox360,
            platform);

        Console.WriteLine($"Platform: {platform}");
    }

    [Fact]
    public void DetectsWorldOfOutlawsAsXex2()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var format = reader.DetectExecutableFormat();

        Assert.Equal(
            XISO.OriginalXbox.XboxExecutableFormat.Xex2,
            format);

        Console.WriteLine($"Executable format: {format}");
    }

    [Fact]
    public void ReadsWorldOfOutlawsXex2Header()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var executable = reader.FindDefaultExecutable();

        Assert.NotNull(executable);

        var headerBytes =
            reader.ReadFilePrefix(executable, 24);

        var xexReader =
            new XISO.OriginalXbox.Xex2Reader();

        var header =
            xexReader.ReadHeader(headerBytes);

        Assert.Equal(24, headerBytes.Length);
        Assert.Equal(0x00000001U, header.ModuleFlags);
        Assert.Equal(0x00003000U, header.PeDataOffset);
        Assert.Equal(0x00000000U, header.Reserved);
        Assert.Equal(0x00000090U, header.SecurityInfoOffset);
        Assert.Equal(15U, header.OptionalHeaderCount);

        Console.WriteLine(
            $"Module flags: 0x{header.ModuleFlags:X8}");

        Console.WriteLine(
            $"PE data offset: 0x{header.PeDataOffset:X8}");

        Console.WriteLine(
            $"Reserved: 0x{header.Reserved:X8}");

        Console.WriteLine(
            $"Security info offset: 0x{header.SecurityInfoOffset:X8}");

        Console.WriteLine(
            $"Optional header count: {header.OptionalHeaderCount}");
    }

    [Fact]
    public void FindsWorldOfOutlawsXex2ExecutionIdHeader()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var executable =
            reader.FindDefaultExecutable();

        Assert.NotNull(executable);

        var headerBytes =
            reader.ReadFilePrefix(executable, 144);

        var xexReader =
            new XISO.OriginalXbox.Xex2Reader();

        var header =
            xexReader.FindOptionalHeader(
                headerBytes,
                0x000400,
                0x06);

        Assert.NotNull(header);

        Assert.Equal(0x00040006U, header.Key);
        Assert.Equal(0x000400U, header.Type);
        Assert.Equal((byte)0x06, header.FormatCode);
        Assert.Equal(0x00001598U, header.Value);

        Console.WriteLine(
            $"Execution ID offset: 0x{header.Value:X8}");
    }
    [Fact]
    public void ReadsWorldOfOutlawsXex2ExecutionId()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var executable =
            reader.FindDefaultExecutable();

        Assert.NotNull(executable);

        var xexData =
            reader.ReadFile(executable);

        var xexReader =
            new XISO.OriginalXbox.Xex2Reader();

        var executionIdHeader =
            xexReader.FindOptionalHeader(
                xexData,
                0x000400,
                0x06);

        Assert.NotNull(executionIdHeader);

        var executionIdData =
            xexData.AsSpan(
                (int)executionIdHeader.Value,
                24)
            .ToArray();

        Console.WriteLine(
            $"Execution ID bytes: {Convert.ToHexString(executionIdData)}");

        var executionId =
            xexReader.ReadExecutionId(xexData);

        Assert.Equal(0x2E39C196U, executionId.MediaId);
        Assert.Equal(3U, executionId.Version);
        Assert.Equal(3U, executionId.BaseVersion);
        Assert.Equal(0x54510835U, executionId.TitleId);
        Assert.Equal((byte)0x00, executionId.Platform);
        Assert.Equal((byte)0x00, executionId.ExecutableType);
        Assert.Equal((byte)1, executionId.DiscNumber);
        Assert.Equal((byte)1, executionId.DiscCount);
        Assert.Equal(0x00000000U, executionId.SaveGameId);
        Console.WriteLine(
            $"Media ID: 0x{executionId.MediaId:X8}");

        Console.WriteLine(
            $"Version: 0x{executionId.Version:X8}");

        Console.WriteLine(
            $"Base version: 0x{executionId.BaseVersion:X8}");

        Console.WriteLine(
            $"Title ID: 0x{executionId.TitleId:X8}");

        Console.WriteLine(
            $"Platform: 0x{executionId.Platform:X2}");

        Console.WriteLine(
            $"Executable type: 0x{executionId.ExecutableType:X2}");

        Console.WriteLine(
            $"Disc number: {executionId.DiscNumber}");

        Console.WriteLine(
            $"Disc count: {executionId.DiscCount}");

        Console.WriteLine(
            $"Save game ID: 0x{executionId.SaveGameId:X8}");
    }
    [Fact]
    public void ReadsWorldOfOutlawsXex2OptionalHeaders()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var executable =
            reader.FindDefaultExecutable();

        Assert.NotNull(executable);

        var headerBytes =
            reader.ReadFilePrefix(executable, 144);

        var xexReader =
            new XISO.OriginalXbox.Xex2Reader();

        var headers =
            xexReader.ReadOptionalHeaders(headerBytes);

        Assert.Equal(15, headers.Count);

        for (int i = 0; i < headers.Count; i++)
        {
            Console.WriteLine(
                $"Optional header {i + 1}: " +
                $"Key=0x{headers[i].Key:X8}, " +
                $"Type=0x{headers[i].Type:X6}, " +
                $"FormatCode=0x{headers[i].FormatCode:X2}, " +
                $"Value=0x{headers[i].Value:X8}");
        }
    }
    [Fact]
    public void AnalyzesWorldOfOutlawsGameImage()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        var analyzer =
            new XISO.OriginalXbox.XboxGameImageAnalyzer();

        var result =
            analyzer.Analyze(isoPath);

        Assert.Equal(
            "World of Outlaws Sprint Cars (EU).iso",
            result.FileName);

        Assert.Equal(
            XISO.Core.Detection.XboxPlatform.Xbox360,
            result.Platform);

        Assert.Equal(
            "default.xex",
            result.Executable);

        Assert.Equal(
            "54510835",
            result.TitleId);

        Assert.Equal(
            "3",
            result.Version);

        Assert.Equal(
            new FileInfo(isoPath).Length,
            result.FileSize);
    }
}
