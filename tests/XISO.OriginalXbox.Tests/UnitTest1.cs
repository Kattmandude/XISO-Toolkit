using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using XISO.Core.Detection;
using XISO.OriginalXbox.Xdvdfs;
using static System.Net.Mime.MediaTypeNames;

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
            new XISO.Core.Metadata.Xex2Reader();

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
            new XISO.Core.Metadata.Xex2Reader();

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
            new XISO.Core.Metadata.Xex2Reader();

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
            new XISO.Core.Metadata.Xex2Reader();

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
    [Fact]
    public void AnalyzesWorldOfOutlawsUsaAndEuropeReleases()
    {
        const string usaIsoPath =
            @"F:\Downloads\World of Outlaws - Sprint Cars (USA).iso";

        const string europeIsoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        var analyzer =
            new XISO.OriginalXbox.XboxGameImageAnalyzer();

        var usa =
            analyzer.Analyze(usaIsoPath);

        var europe =
            analyzer.Analyze(europeIsoPath);

        Assert.Equal(
            XISO.Core.Detection.XboxPlatform.Xbox360,
            usa.Platform);

        Assert.Equal(
            XISO.Core.Detection.XboxPlatform.Xbox360,
            europe.Platform);

        Assert.Equal(
            usa.TitleId,
            europe.TitleId);

        Assert.NotEmpty(usa.MediaId);
        Assert.NotEmpty(europe.MediaId);

        Console.WriteLine(
            $"USA:    Title ID={usa.TitleId}, Media ID={usa.MediaId}");

        Console.WriteLine(
            $"Europe: Title ID={europe.TitleId}, Media ID={europe.MediaId}");
    }
    [Fact]
    public void FindsWorldOfOutlawsInLocalTitleDatabase()
    {
        const string databasePath =
            @"F:\Projects\XISO-Toolkit\src\XISO.Core\XISO.Core\Data\GameTitles.json";

        var database =
            new XISO.Core.Data.GameTitleDatabase(databasePath);

        var result =
            database.Find(
                XISO.Core.Detection.XboxPlatform.Xbox360,
                "54510835");

        Assert.NotNull(result);

        Assert.Equal(
            "World of Outlaws: Sprint Cars",
            result.Title);

        Assert.Contains(
            result.Releases,
            release =>
                release.MediaId == "15B55E58" &&
                release.Region == "USA");

        Assert.Contains(
            result.Releases,
            release =>
                release.MediaId == "2E39C196" &&
                release.Region == "Europe");

        Console.WriteLine(
            $"Title: {result.Title}");

        foreach (var release in result.Releases)
        {
            Console.WriteLine(
                $"Media ID: {release.MediaId} | Region: {release.Region}");
        }
    }

    [Fact]
    public void LoadsToolkitConfiguration()
    {
        const string configurationPath =
            @"F:\Projects\XISO-Toolkit\src\XISO.Core\XISO.Core\Data\ToolkitConfiguration.json";

        var store =
            new XISO.Core.Configuration.ToolkitConfigurationStore();

        var result =
            store.Load(configurationPath);

        Assert.NotNull(result);
        Assert.Equal(2, result.GameLibraries.Count);

        var xbox360Library =
            result.GameLibraries.Single(
                library =>
                    library.Platform ==
                    XISO.Core.Detection.XboxPlatform.Xbox360);

        Assert.Equal(
            "Xbox 360 Games",
            xbox360Library.Name);

        Assert.Equal(
            @"F:\Emulators\Xenia\games",
            xbox360Library.Path);

        var originalXboxLibrary =
            result.GameLibraries.Single(
                library =>
                    library.Platform ==
                    XISO.Core.Detection.XboxPlatform.OriginalXbox);

        Assert.Equal(
            "Original Xbox Games",
            originalXboxLibrary.Name);

        Assert.Equal(
            @"F:\Emulators\Xemu\games",
            originalXboxLibrary.Path);
    }

    [Fact]
    public void SavesAndLoadsToolkitConfiguration()
    {
        var configurationPath =
            Path.Combine(
                Path.GetTempPath(),
                $"XISO-Toolkit-Test-{Guid.NewGuid():N}.json");

        try
        {
            var original =
                new XISO.Core.Configuration.ToolkitConfiguration
                {
                    GameLibraries =
                    [
                        new XISO.Core.Models.GameLibrary
                        {
                            Name = "Xbox 360 Test Library",
                            Platform =
                                XISO.Core.Detection.XboxPlatform.Xbox360,
                            Path = @"D:\Games\Xbox 360"
                        },
                        new XISO.Core.Models.GameLibrary
                        {
                            Name = "Original Xbox Test Library",
                            Platform =
                                XISO.Core.Detection.XboxPlatform.OriginalXbox,
                            Path = @"E:\Games\Original Xbox"
                        }
                    ]
                };

            var store =
                new XISO.Core.Configuration.ToolkitConfigurationStore();

            store.Save(
                configurationPath,
                original);

            var savedJson =
                File.ReadAllText(configurationPath);

            Assert.Contains(
                @"""Platform"": ""Xbox360""",
                savedJson);

            Assert.Contains(
                @"""Platform"": ""OriginalXbox""",
                savedJson);

            var loaded =
                store.Load(configurationPath);

            Assert.Equal(
                2,
                loaded.GameLibraries.Count);

            Assert.Equal(
                original.GameLibraries[0].Name,
                loaded.GameLibraries[0].Name);

            Assert.Equal(
                original.GameLibraries[0].Platform,
                loaded.GameLibraries[0].Platform);

            Assert.Equal(
                original.GameLibraries[0].Path,
                loaded.GameLibraries[0].Path);

            Assert.Equal(
                original.GameLibraries[1].Name,
                loaded.GameLibraries[1].Name);

            Assert.Equal(
                original.GameLibraries[1].Platform,
                loaded.GameLibraries[1].Platform);

            Assert.Equal(
                original.GameLibraries[1].Path,
                loaded.GameLibraries[1].Path);
        }
        finally
        {
            if (File.Exists(configurationPath))
            {
                File.Delete(configurationPath);
            }
        }
    }
    [Fact]
    public void SavesAndLoadsProcessedIsoRecord()
    {
        var databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"ProcessedIsos-{Guid.NewGuid():N}.json");

        try
        {
            var records =
                new List<XISO.Core.Models.ProcessedIsoRecord>
                {
                    new()
                    {
                        IsoPath =
                            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso",

                        IsoFileName =
                            "World of Outlaws Sprint Cars (EU).iso",

                        IsoFileSize =
                            123456789,

                        ProcessedAt =
                            new DateTime(
                                2026,
                                10,
                                5,
                                12,
                                0,
                                0,
                                DateTimeKind.Utc),

                        Platform =
                            XISO.Core.Detection.XboxPlatform.Xbox360,

                        TitleId =
                            "54510835",

                        MediaId =
                            "2E39C196",

                        Title =
                            "World of Outlaws: Sprint Cars",

                        ProcessingStatus =
                            XISO.Core.Models.OperationStatus.Completed,

                        InstallationMethod =
                            XISO.Core.Models.InstallationMethod.ExtractGameFiles,

                        InstallationPath =
                            @"F:\Emulators\Xenia\games\World of Outlaws Sprint Cars (EU)",

                        TitleUpdates =
                        [
                            new XISO.Core.Models.TitleUpdateRecord
                            {
                                Number = 1,
                                PackageName =
                                    "TU_16L61VA_0000008000000.0000000000102",
                                Status =
                                    XISO.Core.Models.OperationStatus.Completed
                            }
                        ]
                    }
                };

            var database =
                new XISO.Core.Data.ProcessedIsoDatabase();

            database.Save(
                databasePath,
                records);

            var loaded =
                database.Load(databasePath);

            Assert.Single(loaded);

            var record = loaded[0];

            Assert.Equal(
                @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso",
                record.IsoPath);

            Assert.Equal(
                "World of Outlaws Sprint Cars (EU).iso",
                record.IsoFileName);

            Assert.Equal(
                123456789,
                record.IsoFileSize);

            Assert.Equal(
                XISO.Core.Detection.XboxPlatform.Xbox360,
                record.Platform);

            Assert.Equal(
                "54510835",
                record.TitleId);

            Assert.Equal(
                "2E39C196",
                record.MediaId);

            Assert.Equal(
                "World of Outlaws: Sprint Cars",
                record.Title);

            Assert.Equal(
                XISO.Core.Models.OperationStatus.Completed,
                record.ProcessingStatus);

            Assert.Equal(
                XISO.Core.Models.InstallationMethod.ExtractGameFiles,
                record.InstallationMethod);

            Assert.Equal(
                @"F:\Emulators\Xenia\games\World of Outlaws Sprint Cars (EU)",
                record.InstallationPath);

            Assert.Single(record.TitleUpdates);

            Assert.Equal(
                1,
                record.TitleUpdates[0].Number);

            Assert.Equal(
                "TU_16L61VA_0000008000000.0000000000102",
                record.TitleUpdates[0].PackageName);

            Assert.Equal(
                XISO.Core.Models.OperationStatus.Completed,
                record.TitleUpdates[0].Status);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
    [Fact]
    public void EnumeratesFilesFromXboxIso()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader =
            new XISO.OriginalXbox.XboxIsoReader(isoPath);

        var files =
            reader.EnumerateFiles();

        Assert.NotEmpty(files);

        var defaultExecutable =
            files.FirstOrDefault(file =>
                string.Equals(
                    file.RelativePath,
                    "default.xex",
                    StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(defaultExecutable);

        Assert.Equal(
            "default.xex",
            defaultExecutable.Entry.Name);

        var dataFile =
            files.FirstOrDefault(file =>
                string.Equals(
                    file.RelativePath,
                    "data1.wad",
                    StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(dataFile);

        Assert.Equal(
            "data1.wad",
            dataFile.Entry.Name);

        Assert.All(
            files,
            file =>
            {
                Assert.False(
                    Path.IsPathRooted(file.RelativePath));

                Assert.False(
                    string.IsNullOrWhiteSpace(file.RelativePath));
            });
    }
    [Fact]
    public void ExtractsGameFilesFromXboxIso()
    {
        const string sourceIso =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        string testRoot =
            Path.Combine(
                Path.GetTempPath(),
                "XISO-Toolkit-Test-" + Guid.NewGuid().ToString("N"));

        string testIso =
            Path.Combine(
                testRoot,
                "source.iso");

        string destination =
            Path.Combine(
                testRoot,
                "Game");

        try
        {
            Directory.CreateDirectory(testRoot);

            File.Copy(
                sourceIso,
                testIso);

            var request =
                new XISO.Core.Installation.GameInstallationRequest
                {
                    IsoPath = testIso,
                    DestinationFolder = destination,
                    Method = XISO.Core.Models.InstallationMethod.ExtractGameFiles
                };

            var installer =
                new XISO.OriginalXbox.Installation.GameInstaller();

            var result =
                installer.Install(request);

            Assert.Equal(
                Path.GetFullPath(destination),
                result);

            Assert.True(
                File.Exists(
                    Path.Combine(
                        destination,
                        "default.xex")));

            Assert.True(
                File.Exists(
                    Path.Combine(
                        destination,
                        "data1.wad")));

            Assert.True(
                File.Exists(
                    Path.Combine(
                        destination,
                        "data.wad")));

            Assert.True(
                Directory.Exists(
                    Path.Combine(
                        destination,
                        "$SystemUpdate")));

            Assert.True(
                File.Exists(
                    Path.Combine(
                        destination,
                        "nxeart")));

            Assert.True(
                File.Exists(testIso));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(
                    testRoot,
                    true);
            }
        }
    }

    [Fact]
    public void ScansXbox360GameLibrary()
    {
        string testRoot =
            Path.Combine(
                Path.GetTempPath(),
                "XISO-Toolkit-Test-" + Guid.NewGuid().ToString("N"));

        string gameFolder =
            Path.Combine(
                testRoot,
                "World of Outlaws - Sprint Cars");

        string nonGameFolder =
            Path.Combine(
                testRoot,
                "Not A Game");

        try
        {
            Directory.CreateDirectory(gameFolder);
            Directory.CreateDirectory(nonGameFolder);

            byte[] xexData = new byte[0x15B0];

            // XEX2 header.
            WriteUInt32BigEndian(xexData, 0x00, 0x58455832);
            WriteUInt32BigEndian(xexData, 0x04, 0x00000001);
            WriteUInt32BigEndian(xexData, 0x08, 0x00003000);
            WriteUInt32BigEndian(xexData, 0x0C, 0x00000000);
            WriteUInt32BigEndian(xexData, 0x10, 0x00000090);
            WriteUInt32BigEndian(xexData, 0x14, 1);

            // Execution ID optional header.
            WriteUInt32BigEndian(xexData, 0x18, 0x00040006);
            WriteUInt32BigEndian(xexData, 0x1C, 0x00001598);

            // European World of Outlaws Execution ID.
            byte[] executionId =
            [
                0x2E, 0x39, 0xC1, 0x96,
                0x00, 0x00, 0x00, 0x03,
                0x00, 0x00, 0x00, 0x03,
                0x54, 0x51, 0x08, 0x35,
                0x00, 0x00, 0x01, 0x01,
                0x00, 0x00, 0x00, 0x00
            ];

            Array.Copy(
                executionId,
                0,
                xexData,
                0x1598,
                executionId.Length);

            File.WriteAllBytes(
                Path.Combine(gameFolder, "default.xex"),
                xexData);

            var scanner =
                new XISO.Xbox360.Installation.GameLibraryScanner();

            var games =
                scanner.Scan(testRoot);

            var game =
                Assert.Single(games);

            Assert.Equal(
                Path.GetFullPath(gameFolder),
                game.InstallationPath);

            Assert.Equal(
                "World of Outlaws - Sprint Cars",
                game.FolderName);

            Assert.Equal(
                XISO.Core.Detection.XboxPlatform.Xbox360,
                game.Platform);

            Assert.Equal(
                "default.xex",
                game.Executable);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(
                    testRoot,
                    true);
            }
        }
    }
    [Fact]
    public void MovesAndRenamesXboxIso()
    {
        const string sourceIso =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        string testRoot =
            Path.Combine(
                Path.GetTempPath(),
                "XISO-Toolkit-Test-" + Guid.NewGuid().ToString("N"));

        string testIso =
            Path.Combine(
                testRoot,
                "original.iso");

        string destination =
            Path.Combine(
                testRoot,
                "World of Outlaws - Sprint Cars");

        const string renamedIso =
            "World of Outlaws - Sprint Cars.iso";

        try
        {
            Directory.CreateDirectory(testRoot);

            File.Copy(
                sourceIso,
                testIso);

            var request =
                new XISO.Core.Installation.GameInstallationRequest
                {
                    IsoPath = testIso,
                    DestinationFolder = destination,
                    Method = XISO.Core.Models.InstallationMethod.MoveIsoImage,
                    IsoFileName = renamedIso
                };

            var installer =
                new XISO.OriginalXbox.Installation.GameInstaller();

            var result =
                installer.Install(request);

            string expectedPath =
                Path.GetFullPath(
                    Path.Combine(
                        destination,
                        renamedIso));

            Assert.Equal(
                expectedPath,
                result);

            Assert.True(
                File.Exists(expectedPath));

            Assert.False(
                File.Exists(testIso));
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(
                    testRoot,
                    true);
            }
        }
    }
    private static void WriteUInt32BigEndian(
        byte[] data,
        int offset,
        uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
    [Fact]
    public void ReadsMysteryOriginalXboxXbeHeader()
    {
        const string isoPath =
            @"F:\Downloads\Some Random Game.iso";

        using var reader =
            new XboxIsoReader(isoPath);

        var executable =
            reader.FindDefaultExecutable();

        Assert.NotNull(executable);
        Assert.Equal(
            "default.xbe",
            executable.Name,
            ignoreCase: true);

        var xbeData =
            reader.ReadFile(executable);

        var xbeReader =
            new XbeReader(xbeData);

        Console.WriteLine("XBE Header:");

        for (int offset = 0; offset < 0x200; offset += 16)
        {
            int length =
                Math.Min(16, xbeData.Length - offset);

            Console.WriteLine(
                $"{offset:X8}  {Convert.ToHexString(xbeData, offset, length)}");
        }

        Console.WriteLine("XBE Certificate:");

        uint certificateOffset =
            xbeReader.CertificateAddress -
            xbeReader.BaseAddress;

        byte[] certificate =
            xbeData[
                checked((int)certificateOffset)..checked((int)certificateOffset + (int)xbeReader.CertificateSize)
            ];

        for (int offset = 0; offset < certificate.Length; offset += 16)
        {
            int length =
                Math.Min(16, certificate.Length - offset);

            Console.WriteLine(
                $"{offset:X8}  {Convert.ToHexString(certificate, offset, length)}");
        }

        Assert.NotEqual(
            0u,
            xbeReader.BaseAddress);

        Assert.NotEqual(
            0u,
            xbeReader.CertificateAddress);

        Console.WriteLine(
            $"XBE Base Address: 0x{xbeReader.BaseAddress:X8}");

        Console.WriteLine(
            $"XBE Certificate Address: 0x{xbeReader.CertificateAddress:X8}");

        Console.WriteLine(
            $"XBE Certificate Size: 0x{xbeReader.CertificateSize:X8}");

        Console.WriteLine(
            $"XBE Disk Number: {xbeReader.DiskNumber}");

        Console.WriteLine(
            $"XBE Version: 0x{xbeReader.Version:X8}");

        Console.WriteLine(
            $"XBE MD5: {xbeReader.Md5}");

        Console.WriteLine(
            $"XBE Title ID: 0x{xbeReader.TitleId:X8}");
        Console.WriteLine(
            $"XBE Allowed Media: 0x{xbeReader.AllowedMedia:X8}");
        Console.WriteLine(
            $"XBE Game Ratings: 0x{xbeReader.GameRatings:X8}");

        Console.WriteLine("XBE Alternate Title IDs:");

        for (int i = 0; i < xbeReader.AlternateTitleIds.Count; i++)
        {
            Console.WriteLine(
                $"  [{i}] 0x{xbeReader.AlternateTitleIds[i]:X8}");
        }

        Console.WriteLine(
            $"XBE Title Name: {xbeReader.TitleName}");

        Console.WriteLine(
            $"XBE Game Region: 0x{xbeReader.GameRegion:X8}");
    }
    [Fact]
    public void LooksUpOriginalXboxReleaseByXbeMd5()
    {
        const string databasePath =
            @"F:\Projects\XISO-Toolkit\.xdb-inspect\XISO-OriginalXbox-Releases.db";

        const string xbeMd5 =
            "A997EA4883A016BC26B63D9035FF56AA";

        var database =
            new OriginalXboxReleaseDatabase(databasePath);

        var release =
            database.FindByXbeMd5(xbeMd5);

        Assert.NotNull(release);

        Assert.Equal(
            "4553001A",
            release.TitleId);

        Assert.Equal(
            "ES-026",
            release.SerialNumber);

        Assert.Equal(
            "ES02609E",
            release.Xmid);

        Assert.Equal(
            "L'Entraîneur 5: Saison 04/05",
            release.FullName);

        Assert.Equal(
            "L'entraineur 5",
            release.TitleName);

        Assert.Equal(
            "(4) PAL",
            release.Region);

        Assert.Equal(
            "0x00000009",
            release.Version);

        Console.WriteLine(
            $"Release: {release.FullName}");

        Console.WriteLine(
            $"Region: {release.Region}");

        Console.WriteLine(
            $"Serial: {release.SerialNumber}");

        Console.WriteLine(
            $"XMID: {release.Xmid}");

        Console.WriteLine(
            $"Version: {release.Version}");
    }
}
