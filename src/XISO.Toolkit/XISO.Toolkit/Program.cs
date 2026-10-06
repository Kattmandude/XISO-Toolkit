using XISO.Core.Configuration;
using XISO.Core.Detection;
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
    Console.WriteLine($"Region: {game.ReleaseRegion}");
    Console.WriteLine($"Title ID: {game.TitleId}");
    Console.WriteLine(
        $"Platform: {(game.Platform == XboxPlatform.OriginalXbox ? "Original Xbox" : game.Platform.ToString())}");
    Console.WriteLine($"Executable: {game.Executable}");

    if (game.Platform == XboxPlatform.Xbox360)
    {
        Console.WriteLine($"Media ID: {game.MediaId}");
        Console.WriteLine($"Version: {game.Version}");
    }
    else if (game.Platform == XboxPlatform.OriginalXbox)
    {
        Console.WriteLine($"Serial: {game.SerialNumber}");
        Console.WriteLine($"XMID: {game.Xmid}");
        Console.WriteLine($"XBE MD5: {game.XbeMd5}");
        var alternateTitleIds = game.AlternateTitleIds.Where(id => id != 0).ToList();
        if (alternateTitleIds.Count > 0) {
            Console.WriteLine("Alternate Title IDs:");
            foreach (var alternateTitleId in alternateTitleIds) {
                Console.WriteLine($"  0x{alternateTitleId:X8}");
            }
        }
        Console.WriteLine($"XBE Region Mask: 0x{game.XbeRegionMask:X8}");
        Console.WriteLine($"XBE Region: {game.XbeRegion}");
        Console.WriteLine($"Allowed Media: 0x{game.AllowedMedia:X8}");
        PrintXbeAllowedMedia(game.AllowedMedia);
        Console.WriteLine($"Version: {game.Version}");
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
                Console.WriteLine($"    XBE Region Mask: 0x{cdxGame.XbeRegionMask:X8}");
                Console.WriteLine($"    XBE Region: {cdxGame.XbeRegion}");
                Console.WriteLine($"    XBE Certificate Version: {cdxGame.Version}");
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
