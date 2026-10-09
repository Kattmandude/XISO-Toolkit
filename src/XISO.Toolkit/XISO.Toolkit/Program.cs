
using XISO.Toolkit;
using XISO.Core.Configuration;
using XISO.Core.Detection;
using XISO.Core.Models;
using XISO.OriginalXbox;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        RunAsync(args).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(string[] args)
    {
        var configurationPath = Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "ToolkitConfiguration.json");

        var configurationStore = new ToolkitConfigurationStore();
        var configuration = configurationStore.Load(configurationPath);

        var form = new MainForm(
            installAction: () =>
                GameInstallationWorkflow.RunAsync(configuration),

            analyzeAction: RunImageAnalysisMenuOptionAsync,

            updateAction: RunMobCatMenuOptionAsync,

            startupAction: CheckMobCatAtStartupAsync,

            initialAction: args.Length > 0 &&
                !string.Equals(args[0], "--update-mobcat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase)
                    ? () => AnalyzeIsoAsync(args[0])
                    : null,

            skipStartupAction: args.Length > 0 &&
                (string.Equals(args[0], "--update-mobcat", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase)));

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(new GuiTextWriter(form));
            Console.SetError(new GuiTextWriter(form));

            if (args.Length > 0 &&
                string.Equals(args[0], "--update-mobcat", StringComparison.OrdinalIgnoreCase))
            {
                form.Shown += async (_, _) =>
                {
                    await ForceMobCatUpdateAsync();
                };
            }

            Application.Run(form);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    static Task RunInteractiveMenuAsync(ToolkitConfiguration configuration)
{
    // The main window now provides the menu.
    return Task.CompletedTask;
}


static async Task RunImageAnalysisMenuOptionAsync()
{
    string? selectedPath = await Task.Run(() =>
    {
        string? path = null;

        var thread = new Thread(() =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select an Xbox ISO or archive",
                Filter =
                    "Xbox images and archives (*.iso;*.xiso;*.zip;*.7z;*.rar)|*.iso;*.xiso;*.zip;*.7z;*.rar|" +
                    "Disc images (*.iso;*.xiso)|*.iso;*.xiso|" +
                    "Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|" +
                    "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() ==
                System.Windows.Forms.DialogResult.OK)
            {
                path = dialog.FileName;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return path;
    });

    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(selectedPath))
    {
        Console.WriteLine("Image selection cancelled.");
        Pause();
        return;
    }

    await AnalyzeIsoAsync(selectedPath);

    Pause();
}

static async Task RunIsoMenuOptionAsync()
{
    string? isoPath = await Task.Run(() =>
    {
        string? selectedPath = null;

        var thread =
            new Thread(() =>
            {
                using var dialog =
                    new System.Windows.Forms.OpenFileDialog
                    {
                        Title = "Select Xbox ISO",
                        Filter =
                            "Xbox images and archives (*.iso;*.xiso;*.zip;*.7z;*.rar)|*.iso;*.xiso;*.zip;*.7z;*.rar|" +
                            "Disc images (*.iso;*.xiso)|*.iso;*.xiso|" +
                            "Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|" +
                            "All files (*.*)|*.*",
                        CheckFileExists = true,
                        Multiselect = false
                    };

                if (dialog.ShowDialog() ==
                    System.Windows.Forms.DialogResult.OK)
                {
                    selectedPath =
                        dialog.FileName;
                }
            });

        thread.SetApartmentState(
            ApartmentState.STA);

        thread.Start();
        thread.Join();

        return selectedPath;
    });

    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(isoPath))
    {
        Console.WriteLine(
            "ISO selection cancelled.");

        Pause();
        return;
    }

    Console.WriteLine(
        $"Selected ISO: {isoPath}");

    Console.WriteLine();

    await AnalyzeIsoAsync(
        isoPath);

    Pause();
}

static async Task RunArchiveMenuOptionAsync()
{
    string? archivePath = await Task.Run(() =>
    {
        string? selectedPath = null;

        var thread =
            new Thread(() =>
            {
                using var dialog =
                    new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description =
                            "Select the archive folder containing Xbox ISO files",

                        UseDescriptionForTitle =
                            true,

                        ShowNewFolderButton =
                            false
                    };

                if (dialog.ShowDialog() ==
                    System.Windows.Forms.DialogResult.OK)
                {
                    selectedPath =
                        dialog.SelectedPath;
                }
            });

        thread.SetApartmentState(
            ApartmentState.STA);

        thread.Start();
        thread.Join();

        return selectedPath;
    });

    Console.WriteLine();

    if (string.IsNullOrWhiteSpace(archivePath))
    {
        Console.WriteLine(
            "Archive folder selection cancelled.");

        Pause();
        return;
    }

    Console.WriteLine(
        $"Selected archive: {archivePath}");

    Console.WriteLine();

    await AnalyzeArchiveAsync(
        archivePath);

    Pause();
}

static async Task RunMobCatMenuOptionAsync()
{
    Console.WriteLine(
        "Checking MobCat database for an update...");
    Console.WriteLine();

    try
    {
        var mobCatClient =
            new MobCatClient();

        MobCatUpdateResult result =
            await mobCatClient.CheckForUpdateAndInstallAsync();

        if (result.Updated)
        {
            Console.WriteLine(
                "MobCat database updated successfully.");

            Console.WriteLine(
                $"Database: {mobCatClient.DatabasePath}");

            if (result.LocalInfo != null)
            {
                Console.WriteLine(
                    $"Records: {result.LocalInfo.RecordCount:N0}");
            }
        }
        else
        {
            Console.WriteLine(
                result.Reason);

            if (result.LocalInfo != null)
            {
                Console.WriteLine(
                    $"Installed records: {result.LocalInfo.RecordCount:N0}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"MobCat update check failed: {ex.Message}");
    }

    Pause();
}

static async Task CheckMobCatAtStartupAsync()
{
    Console.WriteLine(
        "Checking MobCat database...");

    try
    {
        var mobCatClient =
            new MobCatClient();

        MobCatUpdateResult result =
            await mobCatClient.CheckForUpdateAndInstallAsync();

        if (result.Updated)
        {
            Console.WriteLine(
                "MobCat database updated automatically.");

            if (result.LocalInfo != null)
            {
                Console.WriteLine(
                    $"  Records: {result.LocalInfo.RecordCount:N0}");
            }
        }
        else
        {
            Console.WriteLine(
                $"  {result.Reason}");
        }
    }
    catch (Exception ex)
    {
        /*
         * MobCat is an enhancement to the toolkit, not a reason
         * to prevent the rest of the application from starting.
         */
        Console.WriteLine(
            $"  MobCat update check failed: {ex.Message}");

        Console.WriteLine(
            "  Continuing with the local MobCat database.");
    }

    Console.WriteLine();
}

static async Task ForceMobCatUpdateAsync()
{
    try
    {
        var mobCatClient =
            new MobCatClient();

        Console.WriteLine(
            "Updating MobCat database...");
        Console.WriteLine();

        await mobCatClient.DownloadDatabaseAsync(
            true);

        Console.WriteLine();
        Console.WriteLine(
            "MobCat database updated successfully.");

        Console.WriteLine(
            $"Database: {mobCatClient.DatabasePath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"MobCat database update failed: {ex.Message}");
    }
    }

internal static void PrintGameInformation(GameImageInfo game)
    {
        Console.WriteLine($"ISO: {game.FileName}");
        Console.WriteLine($"Size: {FormatSize(game.FileSize)}");
        Console.WriteLine();

        Console.WriteLine("Game Information:");
        Console.WriteLine($"Title: {game.Title}");
        Console.WriteLine($"Title ID: {game.TitleId}");
        Console.WriteLine($"Platform: {(game.Platform == XboxPlatform.OriginalXbox ? "Original Xbox" : game.Platform.ToString())}");
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

            string? publisherId = PublisherLookup.GetPublisherId(game.Xmid);
            string? publisherName = PublisherLookup.GetPublisherName(game.Xmid);
            string? gameId = PublisherLookup.GetGameId(game.Xmid);
            string? skuId = PublisherLookup.GetSkuId(game.Xmid);
            string? regionId = PublisherLookup.GetRegionId(game.Xmid);

            if (!string.IsNullOrWhiteSpace(publisherId))
                Console.WriteLine($"Publisher ID: {publisherId}");

            if (!string.IsNullOrWhiteSpace(publisherName))
                Console.WriteLine($"Publisher: {publisherName}");

            if (!string.IsNullOrWhiteSpace(gameId))
                Console.WriteLine($"Game ID: {gameId}");

            if (!string.IsNullOrWhiteSpace(skuId))
                Console.WriteLine($"SKU ID: {skuId}");

            if (!string.IsNullOrWhiteSpace(regionId))
                Console.WriteLine($"Region ID: {regionId}");

            Console.WriteLine($"XBE MD5: {game.XbeMd5}");

            var alternateTitleIds = game.AlternateTitleIds
                .Where(id => id != 0)
                .ToList();

            if (alternateTitleIds.Count > 0)
            {
                Console.WriteLine("Alternate Title IDs:");

                foreach (var alternateTitleId in alternateTitleIds)
                    Console.WriteLine($"  {alternateTitleId:X8}");
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

            foreach (string line in FormatXbeInitFlags(game.XbeInitFlags)
                         .Split(Environment.NewLine))
            {
                Console.WriteLine($"  {line}");
            }

            Console.WriteLine($"XBE Library Version Count: {game.XbeLibraryVersionCount}");

            PrintXbeLibraries(game.XbeLibraryVersions, "");

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

                    foreach (string line in FormatXbeInitFlags(cdxGame.XbeInitFlags)
                                 .Split(Environment.NewLine))
                    {
                        Console.WriteLine($"      {line}");
                    }

                    Console.WriteLine($"    XBE Library Version Count: {cdxGame.XbeLibraryVersionCount}");

                    PrintXbeLibraries(cdxGame.XbeLibraryVersions, "    ");
                }
            }
        }
    }

static async Task AnalyzeIsoAsync(
    string isoPath)
{
    isoPath =
        isoPath.Trim().Trim('"');

    if (!File.Exists(isoPath))
    {
        Console.WriteLine(
            "ISO file not found:");

        Console.WriteLine(
            isoPath);

        return;
    }

    try
    {
        using var source = ArchiveImageSource.Open(isoPath);

        var analyzer = new XboxGameImageAnalyzer();

        var game = analyzer.Analyze(
            source.ImageStream,
            source.ImageName,
            source.ImageLength);

        PrintGameInformation(game);

        if (game.Platform == XboxPlatform.OriginalXbox &&
            !string.IsNullOrWhiteSpace(game.Xmid))
        {
            Console.WriteLine();
            Console.WriteLine(
                "DBox Release Information (https://dbox.tools):");

            try
            {
                var dboxClient =
                    new DBoxClient();

                var dboxDisc =
                    await dboxClient.GetDiscByXmidAsync(
                        game.Xmid);

                if (dboxDisc == null)
                {
                    Console.WriteLine(
                        "  No DBox record found.");
                }
                else
                {
                    Console.WriteLine(
                        $"  Redump ID: {dboxDisc.RedumpId?.ToString() ?? "Unknown"}");

                    Console.WriteLine(
                        $"  Release: {dboxDisc.RedumpName ?? "Unknown"}");

                    Console.WriteLine(
                        $"  DAT Name: {dboxDisc.RedumpDatName ?? "Unknown"}");

                    Console.WriteLine(
                        $"  Release Country: {dboxDisc.RedumpRegion ?? "Unknown"}");

                    Console.WriteLine(
                        $"  Edition: {dboxDisc.RedumpEdition ?? "Unknown"}");

                    Console.WriteLine(
                        $"  Media: {dboxDisc.RedumpMedia ?? "Unknown"}");

                    Console.WriteLine(
                        $"  Category: {dboxDisc.RedumpCategory ?? "Unknown"}");

                    string languages =
                        string.IsNullOrWhiteSpace(
                            dboxDisc.RedumpLanguages)
                            ? "Not specified"
                            : dboxDisc.RedumpLanguages;

                    Console.WriteLine(
                        $"  Languages: {languages}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  DBox lookup failed: {ex.Message}");
            }
        }

        if (game.Platform == XboxPlatform.OriginalXbox &&
            !string.IsNullOrWhiteSpace(game.Xmid))
        {
            Console.WriteLine();
            Console.WriteLine(
                "MobCat Release Information (https://www.mobcat.zip/XboxIDs/):");

            try
            {
                var mobCatClient =
                    new MobCatClient();

                bool databaseAvailable =
                    await mobCatClient.EnsureDatabaseAsync();

                if (!databaseAvailable)
                {
                    Console.WriteLine(
                        "  MobCat database unavailable; lookup skipped.");
                }
                else
                {
                    var mobCatTitle =
                        await mobCatClient.GetTitleByXmidAsync(
                            game.Xmid);

                    if (mobCatTitle == null)
                    {
                        Console.WriteLine(
                            "  No MobCat record found.");
                    }
                    else
                    {
                        static string Display(
                            string? value)
                        {
                            return string.IsNullOrWhiteSpace(value)
                                ? "Not specified"
                                : value;
                        }

                        Console.WriteLine(
                            $"  Title ID: {Display(mobCatTitle.TitleId)}");

                        Console.WriteLine(
                            $"  Serial Number: {Display(mobCatTitle.SerialNumber)}");

                        Console.WriteLine(
                            $"  XMID: {Display(mobCatTitle.Xmid)}");

                        Console.WriteLine(
                            $"  Full Name: {Display(mobCatTitle.FullName)}");

                        Console.WriteLine(
                            $"  Title Name: {Display(mobCatTitle.TitleName)}");

                        Console.WriteLine(
                            $"  Filename: {Display(mobCatTitle.Filename)}");

                        Console.WriteLine(
                            $"  AKA: {Display(mobCatTitle.Aka)}");

                        Console.WriteLine(
                            $"  Features: {Display(mobCatTitle.Features)}");

                        Console.WriteLine(
                            $"  Publisher: {Display(mobCatTitle.Publisher)}");

                        Console.WriteLine(
                            $"  Region: {Display(mobCatTitle.Region)}");

                        Console.WriteLine(
                            $"  XBE Rating: {Display(mobCatTitle.XbeRating)}");

                        Console.WriteLine(
                            $"  Cover Rating: {Display(mobCatTitle.CoverRating)}");

                        Console.WriteLine(
                            $"  Cover Stats: {Display(mobCatTitle.CoverStats)}");

                        Console.WriteLine(
                            $"  UPC: {Display(mobCatTitle.Upc)}");

                        Console.WriteLine(
                            $"  Version: {Display(mobCatTitle.Version)}");

                        Console.WriteLine(
                            $"  Media Type: {Display(mobCatTitle.MediaType)}");

                        Console.WriteLine(
                            $"  Init Flags: {Display(mobCatTitle.InitFlags)}");

                        Console.WriteLine(
                            $"  Entry Point: {Display(mobCatTitle.EntryPoint)}");

                        Console.WriteLine(
                            $"  Cert Timestamp: {Display(mobCatTitle.CertTimestamp)}");

                        Console.WriteLine(
                            $"  XDK Version: {Display(mobCatTitle.XdkVersion)}");

                        Console.WriteLine(
                            $"  Redump MD5: {Display(mobCatTitle.RedumpMd5)}");

                        Console.WriteLine(
                            $"  XBE MD5: {Display(mobCatTitle.XbeMd5)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  MobCat lookup failed: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Analysis failed:");

        Console.WriteLine(
            ex.Message);
    }
}

static async Task AnalyzeArchiveAsync(
    string archivePath)
{
    archivePath =
        archivePath.Trim().Trim('"');

    if (!Directory.Exists(archivePath))
    {
        Console.WriteLine(
            "Archive folder not found:");

        Console.WriteLine(
            archivePath);

        return;
    }

    List<string> isoFiles;

    try
    {
        isoFiles =
            Directory
                .EnumerateFiles(
                    archivePath,
                    "*.iso",
                    SearchOption.AllDirectories)
                .OrderBy(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Failed to scan archive folder: {ex.Message}");

        return;
    }

    Console.WriteLine(
        $"Archive: {archivePath}");

    Console.WriteLine(
        $"ISO files found: {isoFiles.Count}");

    Console.WriteLine();

    if (isoFiles.Count == 0)
    {
        Console.WriteLine(
            "No ISO files were found.");

        return;
    }

    var analyzer =
        new XboxGameImageAnalyzer();

    int successful =
        0;

    int failed =
        0;

    for (int index = 0;
         index < isoFiles.Count;
         index++)
    {
        string isoPath =
            isoFiles[index];

        Console.Write(
            $"[{index + 1}/{isoFiles.Count}] ");

        try
        {
            var game =
                analyzer.Analyze(isoPath);

            successful++;

            string identity;

            if (game.Platform ==
                XboxPlatform.OriginalXbox)
            {
                identity =
                    $"XMID {game.Xmid}";
            }
            else
            {
                identity =
                    $"Media ID {game.MediaId}";
            }

            Console.WriteLine(
                $"{game.Title} | " +
                $"{(game.Platform == XboxPlatform.OriginalXbox ? "Original Xbox" : game.Platform)} | " +
                $"Title ID {game.TitleId} | " +
                $"{identity}");

            Console.WriteLine(
                $"       {isoPath}");
        }
        catch (Exception ex)
        {
            failed++;

            Console.WriteLine(
                $"FAILED: {Path.GetFileName(isoPath)}");

            Console.WriteLine(
                $"       {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        "Archive Check Complete.");

    Console.WriteLine(
        $"Successful: {successful}");

    Console.WriteLine(
        $"Failed: {failed}");

    Console.WriteLine(
        $"Total: {isoFiles.Count}");
}

static void PrintUsage()
{
    Console.WriteLine(
        "Usage:");

    Console.WriteLine(
        "XISO.Toolkit.exe");

    Console.WriteLine(
        "XISO.Toolkit.exe <ISO path>");

    Console.WriteLine(
        "XISO.Toolkit.exe --update-mobcat");
}

static void Pause()
{
    // No console pause is needed in the GUI.
}

static string FormatSize(
    long bytes)
{
    double size =
        bytes;

    string[] units =
    {
        "B",
        "KB",
        "MB",
        "GB",
        "TB"
    };

    int unit =
        0;

    while (size >= 1024 &&
           unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }

    return
        $"{size:0.00} {units[unit]}";
}

static string FormatXbeInitFlags(
    uint flags)
{
    var descriptions =
        new List<string>();

    const uint MountUtilityDrive =
        0x00000001;

    const uint FormatUtilityDrive =
        0x00000002;

    const uint Limit64Megabytes =
        0x00000004;

    const uint DontSetupHardDisk =
        0x00000008;

    const uint DontModifyHardDisk =
        0x00000010;

    const uint UtilityDriveClusterSizeMask =
        0xC0000000;

    if ((flags & MountUtilityDrive) != 0)
        descriptions.Add(
            "Mount utility drive");

    if ((flags & FormatUtilityDrive) != 0)
        descriptions.Add(
            "Format utility drive");

    if ((flags & Limit64Megabytes) != 0)
        descriptions.Add(
            "Limit development kit runtime memory to 64 MB");

    if ((flags & DontSetupHardDisk) != 0)
        descriptions.Add(
            "Don't set up hard disk");

    if ((flags & DontModifyHardDisk) != 0)
        descriptions.Add(
            "Don't modify hard disk");

    uint clusterSize =
        flags &
        UtilityDriveClusterSizeMask;

    switch (clusterSize)
    {
        case 0x40000000:
            descriptions.Add(
                "Utility drive cluster size: 32 KB");
            break;

        case 0x80000000:
            descriptions.Add(
                "Utility drive cluster size: 64 KB");
            break;

        case 0xC0000000:
            descriptions.Add(
                "Utility drive cluster size: 128 KB");
            break;
    }

    const uint KnownFlags =
        MountUtilityDrive |
        FormatUtilityDrive |
        Limit64Megabytes |
        DontSetupHardDisk |
        DontModifyHardDisk |
        UtilityDriveClusterSizeMask;

    uint unknownFlags =
        flags &
        ~KnownFlags;

    if (unknownFlags != 0)
    {
        descriptions.Add(
            $"Unknown flags: 0x{unknownFlags:X8}");
    }

    if (descriptions.Count == 0)
        return "None";

    return string.Join(
        Environment.NewLine,
        descriptions);
}

static string FormatXbeTimestamp(
    uint timestamp)
{
    DateTimeOffset dateTime =
        DateTimeOffset.FromUnixTimeSeconds(
            timestamp);

    return dateTime
        .ToUniversalTime()
        .ToString(
            "yyyy-MM-dd HH:mm:ss 'UTC'");
}

static void PrintXbeLibraries(
    IReadOnlyList<XbeLibraryVersion> libraries,
    string indent)
{
    Console.WriteLine(
        $"{indent}XBE Library Versions:");

    foreach (var library in libraries)
    {
        ushort qfeVersion =
            (ushort)(
                library.Flags &
                0x1FFF);

        ushort approval =
            (ushort)(
                (library.Flags >> 13) &
                0x03);

        bool debugBuild =
            (library.Flags &
             0x8000) != 0;

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
            $"{indent}  {library.Name} " +
            $"{library.MajorVersion}." +
            $"{library.MinorVersion}." +
            $"{library.BuildVersion} " +
            $"(QFE Version: {qfeVersion}, " +
            $"{buildType}, " +
            $"{approvalDescription}, " +
            $"Flags: 0x{library.Flags:X4})");
    }
}

    static void PrintXbeAllowedMedia(
        uint media)
    {
        var documented =
            new (uint Flag, string Name)[]
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

        Console.WriteLine(
            "Documented Media:");

        foreach (var item in documented)
        {
            if ((media & item.Flag) != 0)
            {
                Console.WriteLine(
                    $"  {item.Name}");
            }
        }

        uint undocumented =
            media &
            ~documentedMask;

        if (undocumented != 0)
        {
            Console.WriteLine(
                $"Undocumented Media Bits: 0x{undocumented:X8}");
        }
    }
}


