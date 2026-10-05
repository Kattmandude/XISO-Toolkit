using XISO.Core.Analysis;

Console.WriteLine("XISO Toolkit starting...");
Console.WriteLine();

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("XISO.Toolkit.exe <game folder>");
    return;
}

var folderPath = args[0];

if (!Directory.Exists(folderPath))
{
    Console.WriteLine("Folder not found:");
    Console.WriteLine(folderPath);
    return;
}

var analyzer = new ImageAnalyzer();

var game = analyzer.Analyze(folderPath);

Console.WriteLine($"Title: {game.Title}");
Console.WriteLine($"Platform: {game.Platform}");
Console.WriteLine($"Executable: {game.Executable}");
Console.WriteLine($"Size: {FormatSize(game.FileSize)}");


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