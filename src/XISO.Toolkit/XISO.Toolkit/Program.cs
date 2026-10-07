using XISO.Core.Configuration;
using XISO.Core.Detection;
using XISO.Core.Models;
using XISO.OriginalXbox;

Console.WriteLine("XISO Toolkit starting...");
Console.WriteLine();

var configurationPath =
    Path.Combine(
        AppContext.BaseDirectory,
        "Data",
        "ToolkitConfiguration.json");

var configurationStore =
    new ToolkitConfigurationStore();

var configuration =
    configurationStore.Load(configurationPath);

Console.WriteLine("Game Libraries:");

foreach (var library in configuration.GameLibraries)
{
    Console.WriteLine(
        $"{library.Name}: {library.Platform} | {library.Path}");
}

Console.WriteLine();

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("XISO.Toolkit.exe <ISO path>");
    return;
}

var isoPath = args[0];

if (!File.Exists(isoPath))
{
    Console.WriteLine("ISO file not found:");
    Console.WriteLine(isoPath);
    return;
}

try
{
    var analyzer =
        new XboxGameImageAnalyzer();

    var game =
        analyzer.Analyze(isoPath);

    Console.WriteLine($"ISO: {game.FileName}");
    Console.WriteLine($"Size: {FormatSize(game.FileSize)}");
    Console.WriteLine();

    Console.WriteLine("Game Information:");
    Console.WriteLine($"Title: {game.Title}");
    Console.WriteLine($"Title ID: {game.TitleId}");
    Console.WriteLine(
        $"Platform: {(game.Platform == XboxPlatform.OriginalXbox ? "Original Xbox" : game.Platform.ToString())}");
    Console.WriteLine($"Executable: {game.Executable}");

    if (game.Platform == XboxPlatform.Xbox360)
    {
        Console.WriteLine($"Media ID: {game.MediaId}");
        Console.WriteLine($"XBE Title Version: {game.Version}");
    }
    else if (game.Platform == XboxPlatform.OriginalXbox)
    {
        Console.WriteLine($"Serial: {game.SerialNumber}");
        Console.WriteLine($"XMID: {game.Xmid}");
        Console.WriteLine($"XBE MD5: {game.XbeMd5}");

        var alternateTitleIds =
            game.AlternateTitleIds
                .Where(id => id != 0)
                .ToList();

        if (alternateTitleIds.Count > 0)
        {
            Console.WriteLine("Alternate Title IDs:");

            foreach (var alternateTitleId in alternateTitleIds)
            {
                Console.WriteLine($"  {alternateTitleId:X8}");
            }
        }

        Console.WriteLine($"XBE Region: {game.XbeRegion}");
        Console.WriteLine($"Allowed Media: 0x{game.AllowedMedia:X8}");

        PrintXbeAllowedMedia(game.AllowedMedia);

        Console.WriteLine($"XBE Title Version: {game.Version}");
        Console.WriteLine($"XBE Header Size: {FormatSize(game.XbeSizeOfHeaders)}");
        Console.WriteLine($"XBE Image Size: {FormatSize(game.XbeSizeOfImage)}");
        Console.WriteLine($"XBE Build Date: {FormatXbeTimestamp(game.XbeTimeDate)}");
        Console.WriteLine($"XBE Sections: {game.XbeNumberOfSections}");

        Console.WriteLine("XBE Init Flags:");

        foreach (string line in FormatXbeInitFlags(game.XbeInitFlags).Split(Environment.NewLine))
        {
            Console.WriteLine($"  {line}");
        }

        Console.WriteLine($"XBE Library Version Count: {game.XbeLibraryVersionCount}");

        PrintXbeLibraries(
            game.XbeLibraryVersions,
            "");

        if (game.CdxGames.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("CDX Games:");

            foreach (var cdxGame in game.CdxGames)
            {
                Console.WriteLine($"  {cdxGame.DisplayName}");
                Console.WriteLine($"    Executable: {cdxGame.FileName}");
                Console.WriteLine($"    Title: {cdxGame.Title}");
                Console.WriteLine($"    Title ID: {cdxGame.TitleId}");
                Console.WriteLine($"    Serial: {cdxGame.SerialNumber}");
                Console.WriteLine($"    XMID: {cdxGame.Xmid}");
                Console.WriteLine($"    XBE MD5: {cdxGame.XbeMd5}");
                Console.WriteLine($"    XBE Region: {cdxGame.XbeRegion}");
                Console.WriteLine($"    XBE Title Version: {cdxGame.Version}");
                Console.WriteLine($"    XBE Header Size: {FormatSize(cdxGame.XbeSizeOfHeaders)}");
                Console.WriteLine($"    XBE Image Size: {FormatSize(cdxGame.XbeSizeOfImage)}");
                Console.WriteLine($"    XBE Build Date: {FormatXbeTimestamp(cdxGame.XbeTimeDate)}");
                Console.WriteLine($"    XBE Sections: {cdxGame.XbeNumberOfSections}");

                Console.WriteLine("    XBE Init Flags:");

                foreach (string line in FormatXbeInitFlags(cdxGame.XbeInitFlags).Split(Environment.NewLine))
                {
                    Console.WriteLine($"      {line}");
                }

                Console.WriteLine($"    XBE Library Version Count: {cdxGame.XbeLibraryVersionCount}");

                PrintXbeLibraries(
                    cdxGame.XbeLibraryVersions,
                    "    ");
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Analysis failed:");
    Console.WriteLine(ex.Message);
}

static string FormatSize(long bytes)
{
    double size = bytes;

    string[] units =
    {
        "B",
        "KB",
        "MB",
        "GB",
        "TB"
    };

    int unit = 0;

    while (size >= 1024 && unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }

    return $"{size:0.00} {units[unit]}";
}


static string FormatXbeInitFlags(uint flags)
{
    var descriptions = new List<string>();

    const uint MountUtilityDrive = 0x00000001;
    const uint FormatUtilityDrive = 0x00000002;
    const uint Limit64Megabytes = 0x00000004;
    const uint DontSetupHardDisk = 0x00000008;
    const uint DontModifyHardDisk = 0x00000010;
    const uint UtilityDriveClusterSizeMask = 0xC0000000;

    if ((flags & MountUtilityDrive) != 0)
        descriptions.Add("Mount utility drive");

    if ((flags & FormatUtilityDrive) != 0)
        descriptions.Add("Format utility drive");

    if ((flags & Limit64Megabytes) != 0)
        descriptions.Add("Limit development kit runtime memory to 64 MB");

    if ((flags & DontSetupHardDisk) != 0)
        descriptions.Add("Don't set up hard disk");

    if ((flags & DontModifyHardDisk) != 0)
        descriptions.Add("Don't modify hard disk");

    uint clusterSize = flags & UtilityDriveClusterSizeMask;

    switch (clusterSize)
    {

        case 0x40000000:
            descriptions.Add("Utility drive cluster size: 32 KB");
            break;

        case 0x80000000:
            descriptions.Add("Utility drive cluster size: 64 KB");
            break;

        case 0xC0000000:
            descriptions.Add("Utility drive cluster size: 128 KB");
            break;
    }

    const uint KnownFlags =
        MountUtilityDrive |
        FormatUtilityDrive |
        Limit64Megabytes |
        DontSetupHardDisk |
        DontModifyHardDisk |
        UtilityDriveClusterSizeMask;

    uint unknownFlags = flags & ~KnownFlags;

    if (unknownFlags != 0)
        descriptions.Add($"Unknown flags: 0x{unknownFlags:X8}");

    if (descriptions.Count == 0)
        return "None";

    return string.Join(Environment.NewLine, descriptions);
}
static string FormatXbeTimestamp(uint timestamp)
{
    DateTimeOffset dateTime =
        DateTimeOffset.FromUnixTimeSeconds(timestamp);

    return dateTime
        .ToUniversalTime()
        .ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
}
static void PrintXbeLibraries(
    IReadOnlyList<XbeLibraryVersion> libraries,
    string indent)
{
    Console.WriteLine($"{indent}XBE Library Versions:");

    foreach (var library in libraries)
    {
        ushort qfeVersion =
            (ushort)(library.Flags & 0x1FFF);

        ushort approval =
            (ushort)((library.Flags >> 13) & 0x03);

        bool debugBuild =
            (library.Flags & 0x8000) != 0;

        string approvalDescription =
            approval switch
            {
                0 => "Unapproved",
                1 => "Possibly Approved",
                2 => "Approved",
                _ => "Unknown"
            };

        string buildType =
            debugBuild
                ? "Debug"
                : "Retail";

        Console.WriteLine(
            $"{indent}  {library.Name} {library.MajorVersion}.{library.MinorVersion}.{library.BuildVersion} " +
            $"(QFE Version: {qfeVersion}, {buildType}, {approvalDescription}, Flags: 0x{library.Flags:X4})");
    }
}
static void PrintXbeAllowedMedia(uint media)
{
    var documented = new (uint Flag, string Name)[]
    {
        (0x00000001, "HARD_DISK"),
        (0x00000002, "DVD_X2"),
        (0x00000004, "DVD_CD"),
        (0x00000008, "CD"),
        (0x00000010, "DVD_5_RO"),
        (0x00000020, "DVD_9_RO"),
        (0x00000040, "DVD_5_RW"),
        (0x00000080, "DVD_9_RW"),
        (0x00000100, "DONGLE"),
        (0x00000200, "MEDIA_BOARD"),
        (0x40000000, "NONSECURE_HARD_DISK"),
        (0x80000000, "NONSECURE_MODE")
    };

    const uint documentedMask =
        0x00000001 |
        0x00000002 |
        0x00000004 |
        0x00000008 |
        0x00000010 |
        0x00000020 |
        0x00000040 |
        0x00000080 |
        0x00000100 |
        0x00000200 |
        0x40000000 |
        0x80000000;

    Console.WriteLine("Documented Media:");

    foreach (var item in documented)
    {
        if ((media & item.Flag) != 0)
            Console.WriteLine($"  {item.Name}");
    }

    uint undocumented = media & ~documentedMask;

    if (undocumented != 0)
        Console.WriteLine($"Undocumented Media Bits: 0x{undocumented:X8}");
}
