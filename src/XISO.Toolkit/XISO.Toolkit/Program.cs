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
        Console.WriteLine($"XBE Region Mask: 0x{game.XbeRegionMask:X8}");
        Console.WriteLine($"Version: {game.Version}");
    }

    Console.WriteLine($"ISO: {game.FileName}");
    Console.WriteLine($"Size: {FormatSize(game.FileSize)}");
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