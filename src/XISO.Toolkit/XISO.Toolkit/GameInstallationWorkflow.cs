using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XISO.Core.Configuration;
using XISO.Core.Detection;
using XISO.Core.Installation;
using XISO.Core.Models;
using XISO.OriginalXbox;

namespace XISO.Toolkit;

public static class GameInstallationWorkflow
{
    internal static async Task RunAsync(
        ToolkitConfiguration configuration,
        MainForm form)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(form);

        string? selectedSourcePath;

        if (!Program.TryGetLastAnalyzedSourcePath(
            out selectedSourcePath))
        {
            selectedSourcePath = OnStaThread(() =>
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Select an Xbox game ISO",
                    Filter = "Xbox images and archives (*.iso;*.xiso;*.zip;*.7z;*.rar)|*.iso;*.xiso;*.zip;*.7z;*.rar|Disc images (*.iso;*.xiso)|*.iso;*.xiso|Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All files (*.*)|*.*",
                    InitialDirectory = Program.GetLastImageDirectory(),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                    return null;

                Program.RememberImageDirectory(dialog.FileName);
                return dialog.FileName;
            });
        }

        if (string.IsNullOrWhiteSpace(selectedSourcePath))
            return;

        ArchiveImageSource? archiveSource = null;

        try
        {
            InstallOptions options;
            GameImageInfo game;
            string title;

            while (true)
            {
                bool hasCachedAnalysis =
                    Program.TryGetCachedAnalysis(
                        selectedSourcePath,
                        out _);

                if (!hasCachedAnalysis)
                {
                    form.SetArtwork(null);
                    form.ClearOutput();
                }

                string extension = Path.GetExtension(selectedSourcePath);

                bool isArchive =
                    extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".rar", StringComparison.OrdinalIgnoreCase);

                IProgress<string>? archiveProgress = null;

                if (!hasCachedAnalysis)
                {
                    form.StartActivity(
                        isArchive
                            ? "Opening archive..."
                            : "Opening disc image...");

                    archiveProgress = new Progress<string>(
                        message => form.UpdateActivity(message));
                }

                try
                {
                    archiveSource = await Task.Run(
                        () => ArchiveImageSource.Open(
                            selectedSourcePath,
                            logDetails: !hasCachedAnalysis,
                            progress: archiveProgress));
                }
                catch (Exception ex)
                {
                    form.StopActivity();

                    ShowMessage(
                        $"The selected file could not be opened as an Xbox image or supported archive.\n\n{ex.Message}",
                        "Installation",
                        MessageBoxIcon.Error);
                    return;
                }

                ArchiveImageSource currentSource = archiveSource!;

                bool reusedCachedAnalysis =
                    Program.TryGetCachedAnalysis(
                        selectedSourcePath,
                        out _);

                try
                {
                    game = await Program.AnalyzeOpenedSourceAsync(
                        currentSource,
                        form,
                        reusedCachedAnalysis,
                        printInformation: !reusedCachedAnalysis,
                        analysisStatusMessage:
                            currentSource.IsArchive
                                ? "Analyzing image; decompressing and caching data as needed..."
                                : "Analyzing game image...");
                }
                catch (Exception ex)
                {
                    ShowMessage(
                        $"The ISO could not be analyzed.\n\n{ex.Message}",
                        "Installation",
                        MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    form.StopActivity();
                }

                if (game.Platform is not XboxPlatform.OriginalXbox
                    and not XboxPlatform.Xbox360)
                {
                    ShowMessage(
                        "The game's Xbox platform could not be identified. Installation was cancelled.",
                        "Installation",
                        MessageBoxIcon.Warning);
                    return;
                }

                MobCatTitle? mobCatTitle = null;

                if (!string.IsNullOrWhiteSpace(game.Xmid))
                {
                    try
                    {
                        mobCatTitle = await new MobCatClient()
                            .GetTitleByXmidAsync(game.Xmid);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"MobCat lookup unavailable: {ex.Message}");
                    }
                }

                DBoxDisc? dboxDisc = null;

                var dboxClient = new DBoxClient();

                if (!string.IsNullOrWhiteSpace(game.Xmid))
                {
                    try
                    {
                        dboxDisc = await dboxClient.GetDiscByXmidAsync(
                            game.Xmid);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DBox XMID lookup unavailable: {ex.Message}");
                    }
                }

                if (dboxDisc is null &&
                    !string.IsNullOrWhiteSpace(game.MediaId))
                {
                    try
                    {
                        dboxDisc = await dboxClient.GetDiscByMediaIdAsync(
                            game.MediaId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"DBox Media ID lookup unavailable: {ex.Message}");
                    }
                }

                title = FirstNonEmpty(
                    mobCatTitle?.FullName,
                    mobCatTitle?.TitleName,
                    game.Title,
                    Path.GetFileNameWithoutExtension(currentSource.ImageName));

                GameLibrary? library = configuration.GameLibraries
                    .FirstOrDefault(item => item.Platform == game.Platform);

                string defaultRoot = library?.Path ?? string.Empty;

                if (!reusedCachedAnalysis)
                    await UpdateArtworkAsync(game, form);

                string? x360DbRegion = null;

                if (game.Platform == XboxPlatform.Xbox360 &&
                    !string.IsNullOrWhiteSpace(game.TitleId))
                {
                    try
                    {
                        var x360DbClient = new X360DbClient();

                        var x360Details = await x360DbClient.GetDetailsAsync(
                            game.TitleId);

                        var mediaEntries = x360Details?.Media;

                        bool hasUsableMediaId =
                            !string.IsNullOrWhiteSpace(game.MediaId) &&
                            !string.Equals(
                                game.MediaId.Trim(),
                                "00000000",
                                StringComparison.OrdinalIgnoreCase);

                        var matchingMedia = hasUsableMediaId
                            ? mediaEntries?.FirstOrDefault(media =>
                                string.Equals(
                                    media.MediaId?.Trim(),
                                    game.MediaId.Trim(),
                                    StringComparison.OrdinalIgnoreCase))
                            : null;

                        x360DbRegion = matchingMedia?.Region;

                        if (string.IsNullOrWhiteSpace(x360DbRegion) &&
                            mediaEntries is { Count: > 0 })
                        {
                            string[] knownRegions = mediaEntries
                                .Select(media => media.Region?.Trim())
                                .Where(region => !string.IsNullOrWhiteSpace(region))
                                .Select(region => region!)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();

                            if (knownRegions.Length == 1)
                                x360DbRegion = knownRegions[0];
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            $"X360DB region lookup unavailable: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine("===== INSTALL DIAGNOSTIC =====");
                System.Diagnostics.Debug.WriteLine($"Platform: {game.Platform}");
                System.Diagnostics.Debug.WriteLine($"Title ID: {game.TitleId}");
                System.Diagnostics.Debug.WriteLine($"Media ID: {game.MediaId}");
                System.Diagnostics.Debug.WriteLine($"DBox disc found: {dboxDisc is not null}");
                System.Diagnostics.Debug.WriteLine($"DBox Redump region: {dboxDisc?.RedumpRegion ?? "<null>"}");
                System.Diagnostics.Debug.WriteLine($"X360DB region: {x360DbRegion ?? "<null>"}");
                System.Diagnostics.Debug.WriteLine("===== END INSTALL DIAGNOSTIC =====");

                InstallOptionsResult? result =
                    await ShowInstallOptionsAsync(
                        title,
                        currentSource.ImageName,
                        mobCatTitle?.Region,
                        dboxDisc?.RedumpRegion,
                        x360DbRegion,
                        game.Platform,
                        game.TitleId,
                        game.MediaId,
                        defaultRoot);

                if (!string.IsNullOrWhiteSpace(result?.SelectedSourcePath))
                {
                    archiveSource.Dispose();
                    archiveSource = null;

                    selectedSourcePath = result.SelectedSourcePath;
                    continue;
                }

                if (result?.Options is null)
                    return;

                Program.RememberGameLibraryPath(
                    game.Platform,
                    result.Options.Root);

                options = result.Options;
                break;
            }

            ArchiveImageSource installationSource = archiveSource!;

            bool extractFiles =
                options.Method == InstallationMethod.ExtractGameFiles;

            bool createGameFolder =
                extractFiles || options.CreateGameFolderForImage;

            string folderName = SanitizeFolderName(options.Name);

            if (createGameFolder &&
                string.IsNullOrWhiteSpace(folderName))
            {
                ShowMessage(
                    "Enter a valid game folder name before installing.",
                    "Installation",
                    MessageBoxIcon.Warning);
                return;
            }

            string root;

            try
            {
                root = Path.GetFullPath(options.Root);

                if (!Directory.Exists(root))
                {
                    ShowMessage(
                        "Choose an existing game library folder before continuing.",
                        "Installation",
                        MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowMessage(
                    $"The game library path is invalid.\n\n{ex.Message}",
                    "Installation",
                    MessageBoxIcon.Error);
                return;
            }

            string imageFileName = options.ImageFileName.Trim();

            if (!extractFiles)
            {
                try
                {
                    ValidateImageFileName(imageFileName);
                }
                catch (ArgumentException ex)
                {
                    ShowMessage(
                        $"{ex.Message}\n\nUse a filename ending in .iso or .xiso.",
                        "Installation",
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            string destinationFolder = createGameFolder
                ? Path.Combine(root, folderName)
                : root;

            string outputPath = extractFiles
                ? destinationFolder
                : Path.Combine(destinationFolder, imageFileName);

            if (createGameFolder &&
                (Directory.Exists(destinationFolder) ||
                 File.Exists(destinationFolder)))
            {
                ShowMessage(
                    $"The destination folder already exists:\n\n{destinationFolder}\n\nNothing was overwritten.",
                    "Installation",
                    MessageBoxIcon.Warning);
                return;
            }

            if (!extractFiles &&
                (File.Exists(outputPath) || Directory.Exists(outputPath)))
            {
                ShowMessage(
                    $"The ISO/XISO destination already exists:\n\n{outputPath}\n\nNothing was overwritten.",
                    "Installation",
                    MessageBoxIcon.Warning);
                return;
            }

            string methodDescription = extractFiles
                ? "Extract game contents"
                : "Keep ISO/XISO image";

            var installer =
                new XISO.OriginalXbox.Installation.GameInstaller();

            var request = new GameInstallationRequest
            {
                IsoPath = installationSource.ImagePath,
                DestinationFolder = destinationFolder,
                Method = options.Method,
                IsoFileName = extractFiles
                    ? installationSource.ImageName
                    : imageFileName,
                CreateGameFolderForImage = createGameFolder
            };

            var installationResult = InstallationProgressDialog.Show(
                title,
                $"{methodDescription} ({game.Platform})",
                selectedSourcePath,
                outputPath,
                (progress, cancellationToken) =>
                {
                    string installedPath = installer.Install(
                        installationSource.ImageStream,
                        installationSource.ImageName,
                        request,
                        progress,
                        cancellationToken);

                    var summaryLines = new List<string>
                    {
                        "Installation completed successfully.",
                        $"Installed to: {installedPath}"
                    };

                    if (game.Platform == XboxPlatform.Xbox360 &&
                        options.DownloadTitleUpdates)
                    {
                        summaryLines.Add(string.Empty);
                        summaryLines.Add("XboxUnity Title Update summary");

                        string titleUpdatePath =
                            options.TitleUpdatePath.Trim();

                        if (string.IsNullOrWhiteSpace(titleUpdatePath))
                        {
                            summaryLines.Add(
                                "Title Update download skipped: no destination was selected.");
                            summaryLines.Add("Found: 0");
                            summaryLines.Add("Downloaded: 0");
                            summaryLines.Add("Failed: 1");
                        }
                        else
                        {
                            var downloader = new TitleUpdateDownloader();

                            progress.Report(new InstallationProgress(
                                "Checking XboxUnity",
                                $"Checking XboxUnity for updates for Title ID {game.TitleId}...",
                                null));

                            var search = downloader.CheckForUpdatesAsync(
                                game.TitleId,
                                game.MediaId,
                                cancellationToken)
                                .GetAwaiter()
                                .GetResult();

                            if (!search.Succeeded)
                            {
                                summaryLines.Add(
                                    "XboxUnity check failed: " +
                                    (search.Error ??
                                     "No further details were provided."));
                                summaryLines.Add("Found: unavailable");
                                summaryLines.Add("Downloaded: 0");
                                summaryLines.Add("Failed: 1");
                            }
                            else
                            {
                                progress.Report(new InstallationProgress(
                                    "Checking XboxUnity",
                                    $"Found {search.Updates.Count} matching Title Update(s). Preparing downloads...",
                                    100));

                                var downloadProgress =
                                    new Progress<string>(message =>
                                        progress.Report(
                                            new InstallationProgress(
                                                "Downloading Title Updates",
                                                message,
                                                null)));

                                var downloadResult =
                                    downloader.DownloadAsync(
                                        game.TitleId,
                                        game.MediaId,
                                        titleUpdatePath,
                                        downloadProgress,
                                        cancellationToken,
                                        knownUpdates: search.Updates)
                                    .GetAwaiter()
                                    .GetResult();

                                summaryLines.Add(
                                    $"Found: {downloadResult.Found}");
                                summaryLines.Add(
                                    $"Downloaded: {downloadResult.Downloaded}");
                                summaryLines.Add(
                                    $"Failed: {downloadResult.Failed}");

                                if (downloadResult.Messages.Count > 0)
                                {
                                    summaryLines.Add(string.Empty);
                                    summaryLines.Add("Title Update details:");

                                    summaryLines.AddRange(
                                        downloadResult.Messages);
                                }
                            }
                        }
                    }

                    string finalSummary =
                        string.Join(Environment.NewLine, summaryLines);

                    progress.Report(new InstallationProgress(
                        "Completed",
                        finalSummary,
                        100));

                    return installedPath;
                });

            // The progress window itself now contains the installation
            // and optional Title Update summary. Do not show a second
            // Title Update MessageBox.
            if (!installationResult.Succeeded)
                return;
        }
        finally
        {
            archiveSource?.Dispose();
        }
    }

    private static async Task UpdateArtworkAsync(
        GameImageInfo game,
        MainForm form)
    {
        if (game.Platform != XboxPlatform.Xbox360)
            return;

        try
        {
            var client = new X360DbClient();

            bool databaseAvailable = await client.EnsureDatabaseAsync();

            if (!databaseAvailable)
            {
                Console.WriteLine(
                    "Artwork unavailable: Xbox 360 database is unavailable.");
                return;
            }

            var x360Game =
                await client.GetGameAsync(game.TitleId, game.MediaId);

            var x360Details =
                await client.GetDetailsAsync(game.TitleId);

            string? boxArtUrl =
                x360Details?.Artwork?.BoxArt ?? x360Game?.BoxArt;

            string? artworkPath =
                await client.DownloadBoxArtAsync(game.TitleId, boxArtUrl);

            if (!string.IsNullOrWhiteSpace(artworkPath))
                form.SetArtwork(artworkPath);
            else
                Console.WriteLine("No Xbox 360 artwork was available for this game.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not load game artwork: {ex.Message}");
        }
    }

    private static async Task<InstallOptionsResult?> ShowInstallOptionsAsync(
        string title,
        string imageName,
        string? region,
        string? releaseCountry,
        string? x360DbRegion,
        XboxPlatform platform,
        string titleId,
        string mediaId,
        string defaultRoot)
    {
        var completion = new TaskCompletionSource<InstallOptionsResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        string? usableRegion = string.IsNullOrWhiteSpace(region)
            ? null
            : FormatRegion(region);

        string? usableCountry = string.IsNullOrWhiteSpace(releaseCountry)
            ? null
            : releaseCountry.Trim();

        string originalImageName = Path.GetFileName(imageName);
        string originalImageStem =
            Path.GetFileNameWithoutExtension(originalImageName);
        string originalImageExtension =
            originalImageName.EndsWith(
                ".xiso.iso",
                StringComparison.OrdinalIgnoreCase)
                ? originalImageName[^9..]
                : Path.GetExtension(originalImageName);

        bool isOriginalXbox = platform == XboxPlatform.OriginalXbox;
        bool isXbox360 = platform == XboxPlatform.Xbox360;

        var form = new Form
        {
            Text = "Install Game",
            Width = 700,
            Height = 865,
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var namingGroup = new GroupBox
        {
            Text = "Game Naming Suffix",
            Left = 15,
            Top = 15,
            Width = 650,
            Height = 160
        };

        var noSuffixRadio = new RadioButton
        {
            Text = "No suffix",
            Left = 15,
            Top = 22,
            Width = 600,
            Checked = true
        };

        string? usableX360DbRegion =
            string.IsNullOrWhiteSpace(x360DbRegion)
                ? null
                : x360DbRegion.Trim();

        var regionRadio = new RadioButton
        {
            Text = usableRegion is null
                ? "MobCat region (unavailable)"
                : $"MobCat region: {usableRegion}",
            Left = 15,
            Top = 47,
            Width = 600,
            Visible = isOriginalXbox,
            Enabled = isOriginalXbox && usableRegion is not null
        };

        var countryRadio = new RadioButton
        {
            Text = usableCountry is null
                ? "DBox release country (unavailable)"
                : $"DBox release country: {usableCountry}",
            Left = 15,
            Top = 72,
            Width = 600,
            Visible = isOriginalXbox,
            Enabled = isOriginalXbox && usableCountry is not null
        };

        var dboxRegionRadio = new RadioButton
        {
            Text = usableCountry is null
                ? "DBox region (unavailable)"
                : $"DBox region: {usableCountry}",
            Left = 15,
            Top = 47,
            Width = 600,
            Visible = isXbox360,
            Enabled = isXbox360 && usableCountry is not null
        };

        var x360DbRegionRadio = new RadioButton
        {
            Text = usableX360DbRegion is null
                ? "X360DB region (unavailable)"
                : $"X360DB region: {usableX360DbRegion}",
            Left = 15,
            Top = 72,
            Width = 600,
            Visible = isXbox360,
            Enabled = isXbox360 && usableX360DbRegion is not null
        };

        var countryFormatGroup = new GroupBox
        {
            Text = "Suffix Format",
            Left = 15,
            Top = 98,
            Width = 430,
            Height = 52,
            Enabled = false
        };

        var fullCountryRadio = new RadioButton
        {
            Text = "Full name",
            Left = 10,
            Top = 20,
            Width = 130,
            Checked = true
        };

        var countryCodeRadio = new RadioButton
        {
            Text = "Two-letter country code",
            Left = 160,
            Top = 20,
            Width = 240
        };

        countryFormatGroup.Controls.AddRange(
        [
            fullCountryRadio,
            countryCodeRadio
        ]);

        namingGroup.Controls.AddRange(
        [
            noSuffixRadio,
            countryRadio,
            regionRadio,
            dboxRegionRadio,
            x360DbRegionRadio,
            countryFormatGroup
        ]);

        string? GetSelectedSuffix()
        {
            if (regionRadio.Checked && usableRegion is not null)
            {
                return fullCountryRadio.Checked
                    ? usableRegion
                    : GetCountryListCodes(usableRegion);
            }

            if (countryRadio.Checked && usableCountry is not null)
            {
                return fullCountryRadio.Checked
                    ? usableCountry
                    : GetCountryListCodes(usableCountry);
            }

            if (dboxRegionRadio.Checked && usableCountry is not null)
            {
                return fullCountryRadio.Checked
                    ? usableCountry
                    : GetCountryListCodes(usableCountry);
            }

            if (x360DbRegionRadio.Checked &&
                usableX360DbRegion is not null)
            {
                return fullCountryRadio.Checked
                    ? usableX360DbRegion
                    : GetCountryListCodes(usableX360DbRegion);
            }

            return null;
        }

        string MakeAutomaticName()
        {
            string baseName = FormatTitleForFolder(title);
            string? suffix = GetSelectedSuffix();

            return string.IsNullOrWhiteSpace(suffix)
                ? baseName
                : $"{baseName} ({suffix})";
        }

        var methodGroup = new GroupBox
        {
            Text = "Installation Method",
            Left = 15,
            Top = 180,
            Width = 650,
            Height = 70
        };

        var extractRadio = new RadioButton
        {
            Text = "Extract game contents",
            Left = 15,
            Top = 28,
            Width = 250,
            Checked = true
        };

        var keepImageRadio = new RadioButton
        {
            Text = "Keep ISO/XISO image",
            Left = 300,
            Top = 28,
            Width = 250
        };

        methodGroup.Controls.AddRange(
        [
            extractRadio,
            keepImageRadio
        ]);

        var imageLayoutGroup = new GroupBox
        {
            Text = "ISO/XISO Destination",
            Left = 15,
            Top = 255,
            Width = 650,
            Height = 70,
            Enabled = false
        };

        var libraryRootRadio = new RadioButton
        {
            Text = "Directly in Game library folder",
            Left = 15,
            Top = 27,
            Width = 290
        };

        var gameFolderRadio = new RadioButton
        {
            Text = "Create a game folder",
            Left = 330,
            Top = 27,
            Width = 250,
            Checked = true
        };

        imageLayoutGroup.Controls.AddRange(
        [
            libraryRootRadio,
            gameFolderRadio
        ]);

        var folderNameLabel = new Label
        {
            Text = "Game folder name:",
            Left = 15,
            Top = 335,
            Width = 200
        };

        var nameBox = new TextBox
        {
            Left = 15,
            Top = 358,
            Width = 650
        };

        var imageNameLabel = new Label
        {
            Text = "ISO/XISO filename:",
            Left = 15,
            Top = 395,
            Width = 200,
            Enabled = false
        };

        var imageNameBox = new TextBox
        {
            Left = 15,
            Top = 418,
            Width = 650,
            Text = originalImageName,
            Enabled = false
        };

        var applySuffixToImageCheckBox = new CheckBox
        {
            Text = "Apply naming suffix options to ISO/XISO filename",
            Left = 15,
            Top = 446,
            Width = 450,
            Enabled = false,
            Checked = false
        };

        void RefreshImageNameSuffix()
        {
            if (!applySuffixToImageCheckBox.Checked)
            {
                imageNameBox.Text = originalImageName;
                return;
            }

            string gameName = MakeAutomaticName();

            string extension = originalImageName.EndsWith(
                    ".xiso.iso",
                    StringComparison.OrdinalIgnoreCase)
                ? originalImageName[^9..]
                : Path.GetExtension(originalImageName);

            imageNameBox.Text = gameName + extension;
        }

        void RefreshFolderName()
        {
            bool extractFiles = extractRadio.Checked;
            bool createFolder = gameFolderRadio.Checked;

            folderNameLabel.Enabled =
                extractFiles || (keepImageRadio.Checked && createFolder);

            nameBox.Enabled =
                extractFiles || (keepImageRadio.Checked && createFolder);
        }

        void RefreshImageOptions()
        {
            bool keepImage = keepImageRadio.Checked;

            imageLayoutGroup.Enabled = keepImage;
            imageNameLabel.Enabled = keepImage;
            imageNameBox.Enabled = keepImage;
            applySuffixToImageCheckBox.Enabled = keepImage;

            RefreshFolderName();
        }

        void RefreshNamingOptions()
        {
            countryFormatGroup.Enabled =
                !noSuffixRadio.Checked &&
                (regionRadio.Checked ||
                 countryRadio.Checked ||
                 dboxRegionRadio.Checked ||
                 x360DbRegionRadio.Checked);

            nameBox.Text = MakeAutomaticName();

            if (applySuffixToImageCheckBox.Checked)
                RefreshImageNameSuffix();
        }

        noSuffixRadio.CheckedChanged += (_, _) =>
        {
            if (noSuffixRadio.Checked)
                RefreshNamingOptions();
        };

        regionRadio.CheckedChanged += (_, _) =>
        {
            if (regionRadio.Checked)
                RefreshNamingOptions();
        };

        countryRadio.CheckedChanged += (_, _) =>
        {
            if (countryRadio.Checked)
                RefreshNamingOptions();
        };

        dboxRegionRadio.CheckedChanged += (_, _) =>
        {
            if (dboxRegionRadio.Checked)
                RefreshNamingOptions();
        };

        x360DbRegionRadio.CheckedChanged += (_, _) =>
        {
            if (x360DbRegionRadio.Checked)
                RefreshNamingOptions();
        };

        fullCountryRadio.CheckedChanged += (_, _) =>
        {
            if (fullCountryRadio.Checked)
                RefreshNamingOptions();
        };

        countryCodeRadio.CheckedChanged += (_, _) =>
        {
            if (countryCodeRadio.Checked)
                RefreshNamingOptions();
        };

        applySuffixToImageCheckBox.CheckedChanged += (_, _) =>
            RefreshImageNameSuffix();

        extractRadio.CheckedChanged += (_, _) => RefreshImageOptions();
        keepImageRadio.CheckedChanged += (_, _) => RefreshImageOptions();
        libraryRootRadio.CheckedChanged += (_, _) => RefreshFolderName();
        gameFolderRadio.CheckedChanged += (_, _) => RefreshFolderName();

        RefreshNamingOptions();
        RefreshImageOptions();

        var rootLabel = new Label
        {
            Text = "Game library folder:",
            Left = 15,
            Top = 480,
            Width = 200
        };

        var rootBox = new TextBox
        {
            Left = 15,
            Top = 503,
            Width = 535,
            Text = defaultRoot
        };

        var browseButton = new Button
        {
            Text = "Browse...",
            Left = 560,
            Top = 501,
            Width = 105
        };

        browseButton.Click += (_, _) =>
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Choose the game library folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (Directory.Exists(rootBox.Text))
                folderDialog.SelectedPath = rootBox.Text;

            if (folderDialog.ShowDialog(form) == DialogResult.OK)
                rootBox.Text = folderDialog.SelectedPath;
        };

        // Title Update lookup UI. The list is populated asynchronously
        // after the dialog opens, without downloading any update files.
        var titleUpdateGroup = new GroupBox
        {
            Text = "Available Xbox 360 Title Updates",
            Left = 15,
            Top = 535,
            Width = 650,
            Height = 125,
            Visible = isXbox360
        };

        var titleUpdateStatusLabel = new Label
        {
            Text = "Checking XboxUnity for available updates...",
            Left = 12,
            Top = 22,
            Width = 510,
            Height = 22,
            AutoEllipsis = true
        };

        var titleUpdateRetryButton = new Button
        {
            Text = "Retry",
            Left = 535,
            Top = 18,
            Width = 95,
            Height = 27,
            Visible = false
        };

        var titleUpdateListBox = new ListBox
        {
            Left = 12,
            Top = 48,
            Width = 618,
            Height = 65,
            IntegralHeight = false
        };

        titleUpdateGroup.Controls.AddRange(
        [
            titleUpdateStatusLabel,
            titleUpdateRetryButton,
            titleUpdateListBox
        ]);

        var titleUpdateCheckBox = new CheckBox
        {
            Text = "Install available Title Updates after installation",
            Left = 15,
            Top = 665,
            Width = 500,
            Checked = false,
            Enabled = false,
            Visible = isXbox360
        };

        var titleUpdatePathLabel = new Label
        {
            Text = "Title Update destination:",
            Left = 15,
            Top = 693,
            Width = 250,
            Visible = isXbox360
        };

        var titleUpdatePathBox = new TextBox
        {
            Left = 15,
            Top = 716,
            Width = 535,
            Visible = isXbox360,
            Enabled = false
        };

        bool titleUpdatePathManuallyEdited = false;

        var titleUpdateBrowseButton = new Button
        {
            Text = "Browse...",
            Left = 560,
            Top = 714,
            Width = 105,
            Visible = isXbox360,
            Enabled = false
        };

        titleUpdateCheckBox.CheckedChanged += (_, _) =>
        {
            titleUpdatePathBox.Enabled = titleUpdateCheckBox.Checked;
            titleUpdateBrowseButton.Enabled = titleUpdateCheckBox.Checked;
        };

        titleUpdateBrowseButton.Click += (_, _) =>
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Choose the Title Update destination",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (Directory.Exists(titleUpdatePathBox.Text))
                folderDialog.SelectedPath = titleUpdatePathBox.Text;

            if (folderDialog.ShowDialog(form) == DialogResult.OK)
            {
                titleUpdatePathBox.Text = folderDialog.SelectedPath;
                titleUpdatePathManuallyEdited = true;
            }
        };

        void RefreshTitleUpdatePath()
        {
            if (titleUpdatePathManuallyEdited)
                return;

            string selectedRoot = rootBox.Text.Trim();
            string selectedName = SanitizeFolderName(nameBox.Text.Trim());

            bool createFolder =
                extractRadio.Checked || gameFolderRadio.Checked;

            string gameDestination = createFolder
                ? Path.Combine(selectedRoot, selectedName)
                : selectedRoot;

            titleUpdatePathBox.Text =
                Path.Combine(gameDestination, "TitleUpdate");
        }

        titleUpdatePathBox.TextChanged += (_, _) =>
        {
            if (titleUpdatePathBox.Focused)
                titleUpdatePathManuallyEdited = true;
        };

        nameBox.TextChanged += (_, _) => RefreshTitleUpdatePath();
        rootBox.TextChanged += (_, _) => RefreshTitleUpdatePath();
        extractRadio.CheckedChanged += (_, _) => RefreshTitleUpdatePath();
        keepImageRadio.CheckedChanged += (_, _) => RefreshTitleUpdatePath();
        libraryRootRadio.CheckedChanged += (_, _) => RefreshTitleUpdatePath();
        gameFolderRadio.CheckedChanged += (_, _) => RefreshTitleUpdatePath();

        RefreshTitleUpdatePath();

        IReadOnlyList<TitleUpdateDownloader.AvailableUpdate>
            availableTitleUpdates =
                Array.Empty<TitleUpdateDownloader.AvailableUpdate>();

        using var lookupCancellation = new CancellationTokenSource();

        async Task RefreshAvailableTitleUpdatesAsync()
        {
            if (!isXbox360 || form.IsDisposed)
                return;

            titleUpdateRetryButton.Visible = false;
            titleUpdateListBox.Items.Clear();
            titleUpdateCheckBox.Enabled = false;
            titleUpdateCheckBox.Checked = false;

            titleUpdateStatusLabel.Text =
                "Checking XboxUnity for available updates...";

            if (string.IsNullOrWhiteSpace(titleId) ||
                string.IsNullOrWhiteSpace(mediaId))
            {
                titleUpdateStatusLabel.Text =
                    "Title ID or Media ID is missing.";

                return;
            }

            try
            {
                var downloader = new TitleUpdateDownloader();

                var search = await downloader.CheckForUpdatesAsync(
                    titleId,
                    mediaId,
                    lookupCancellation.Token);

                if (form.IsDisposed)
                    return;

                if (!search.Succeeded)
                {
                    availableTitleUpdates =
                        Array.Empty<TitleUpdateDownloader.AvailableUpdate>();

                    titleUpdateStatusLabel.Text =
                        search.Error ?? "Could not check for updates.";

                    titleUpdateRetryButton.Visible = true;
                    return;
                }

                availableTitleUpdates = search.Updates;

                if (availableTitleUpdates.Count == 0)
                {
                    titleUpdateStatusLabel.Text =
                        "No matching Title Updates were found.";

                    return;
                }

                titleUpdateStatusLabel.Text =
                    $"Found {availableTitleUpdates.Count} matching update(s).";

                foreach (var update in availableTitleUpdates)
                {
                    titleUpdateListBox.Items.Add(
                        $"Version {update.Version} — {update.FileName}");
                }

                titleUpdateCheckBox.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                // The dialog is closing.
            }
            catch (Exception ex)
            {
                if (form.IsDisposed)
                    return;

                availableTitleUpdates =
                    Array.Empty<TitleUpdateDownloader.AvailableUpdate>();

                titleUpdateStatusLabel.Text =
                    $"Unable to check for updates: {ex.Message}";

                titleUpdateRetryButton.Visible = true;
            }
        }

        titleUpdateRetryButton.Click += async (_, _) =>
        {
            titleUpdateRetryButton.Enabled = false;

            try
            {
                await RefreshAvailableTitleUpdatesAsync();
            }
            finally
            {
                if (!form.IsDisposed)
                    titleUpdateRetryButton.Enabled = true;
            }
        };

        var installButton = new Button
        {
            Text = "Install",
            Left = 450,
            Top = 775,
            Width = 100,
            DialogResult = DialogResult.None
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 560,
            Top = 775,
            Width = 105,
            DialogResult = DialogResult.None
        };

        var selectDifferentFileButton = new Button
        {
            Text = "Select Different File...",
            Left = 15,
            Top = 775,
            Width = 180,
            DialogResult = DialogResult.None
        };

        selectDifferentFileButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a different Xbox game ISO",
                Filter = "Xbox images and archives (*.iso;*.xiso;*.zip;*.7z;*.rar)|*.iso;*.xiso;*.zip;*.7z;*.rar|Disc images (*.iso;*.xiso)|*.iso;*.xiso|Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All files (*.*)|*.*",
                InitialDirectory = Program.GetLastImageDirectory(),
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(form) != DialogResult.OK)
                return;

            Program.RememberImageDirectory(dialog.FileName);

            completion.TrySetResult(
                new InstallOptionsResult(null, dialog.FileName));

            form.Close();
        };

        form.Controls.AddRange(
        [
            namingGroup,
            methodGroup,
            imageLayoutGroup,
            folderNameLabel,
            nameBox,
            imageNameLabel,
            imageNameBox,
            applySuffixToImageCheckBox,
            rootLabel,
            rootBox,
            browseButton,
            titleUpdateGroup,
            titleUpdateCheckBox,
            titleUpdatePathLabel,
            titleUpdatePathBox,
            titleUpdateBrowseButton,
            selectDifferentFileButton,
            installButton,
            cancelButton
        ]);

        form.AcceptButton = installButton;
        form.CancelButton = cancelButton;

        installButton.Click += (_, _) =>
        {
            string selectedName = nameBox.Text.Trim();
            string selectedRoot = rootBox.Text.Trim();
            string selectedImageName = imageNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(selectedRoot))
            {
                MessageBox.Show(
                    form,
                    "Choose a game library folder before continuing.",
                    "Installation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            InstallationMethod method = extractRadio.Checked
                ? InstallationMethod.ExtractGameFiles
                : InstallationMethod.MoveIsoImage;

            completion.TrySetResult(
                new InstallOptionsResult(
                    new InstallOptions(
                        selectedName,
                        selectedRoot,
                        method,
                        selectedImageName,
                        gameFolderRadio.Checked,
                        titleUpdateCheckBox.Checked,
                        titleUpdatePathBox.Text.Trim(),
                        availableTitleUpdates),
                    null));

            form.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            completion.TrySetResult(null);
            form.Close();
        };

        form.FormClosed += (_, _) =>
        {
            lookupCancellation.Cancel();
            completion.TrySetResult(null);
        };

        form.Shown += async (_, _) =>
        {
            await RefreshAvailableTitleUpdatesAsync();
        };

        try
        {
            form.Show();

            return await completion.Task;
        }
        finally
        {
            lookupCancellation.Cancel();
            form.Dispose();
        }
    }

    private static void ValidateImageFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            (!fileName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) &&
             !fileName.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The ISO/XISO filename is invalid.");
        }
    }

    private static string FormatTitleForFolder(string title)
    {
        string cleaned = title
            .Replace(":", " - ")
            .Replace("/", "-")
            .Replace("\\", "-");

        foreach (char ch in new[] { '<', '>', '"', '|', '?', '*' })
            cleaned = cleaned.Replace(ch, '-');

        return string.Join(
            " ",
            cleaned.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FormatRegion(string region)
    {
        string cleaned = region.Trim();

        if (cleaned.StartsWith('('))
        {
            int closingParen = cleaned.IndexOf(')');

            if (closingParen > 1 &&
                cleaned.AsSpan(1, closingParen - 1)
                    .ToString().All(char.IsDigit))
            {
                cleaned = cleaned[(closingParen + 1)..].Trim();
            }
        }

        string[] parts = cleaned.Split(
            new[] { '/', ';', ',', '+' },
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        return parts.Length == 0
            ? cleaned
            : string.Join(", ", parts);
    }

    private static string GetCountryListCodes(string countries)
    {
        string[] parts =
            System.Text.RegularExpressions.Regex.Split(
                countries,
                @"\s*(?:\+|,)\s*");

        for (int i = 0; i < parts.Length; i++)
        {
            string item = parts[i].Trim();

            if (item.Length == 0)
                continue;

            if (string.Equals(
                    item,
                    "World",
                    StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "World";
            }
            else if (string.Equals(
                         item,
                         "Europe",
                         StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = "EU";
            }
            else
            {
                parts[i] = GetCountryCodeOrOriginal(item);
            }
        }

        return string.Join(
            ", ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetCountryCodeOrOriginal(string country)
    {
        string cleaned = country.Trim();

        var aliases =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["USA"] = "US",
                ["U.S.A."] = "US",
                ["United States of America"] = "US",
                ["UK"] = "GB",
                ["Great Britain"] = "GB"
            };

        if (aliases.TryGetValue(cleaned, out string? aliasCode))
            return aliasCode;

        foreach (var culture in
                 System.Globalization.CultureInfo.GetCultures(
                     System.Globalization.CultureTypes.SpecificCultures))
        {
            try
            {
                var region =
                    new System.Globalization.RegionInfo(culture.Name);

                if (string.Equals(
                        region.EnglishName,
                        cleaned,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        region.NativeName,
                        cleaned,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return region.TwoLetterISORegionName;
                }
            }
            catch (ArgumentException)
            {
                // Some cultures do not map to a valid region.
            }
        }

        return cleaned;
    }

    private sealed record InstallOptions(
        string Name,
        string Root,
        InstallationMethod Method,
        string ImageFileName,
        bool CreateGameFolderForImage,
        bool DownloadTitleUpdates,
        string TitleUpdatePath,
        IReadOnlyList<TitleUpdateDownloader.AvailableUpdate>
            AvailableTitleUpdates);

    private sealed record InstallOptionsResult(
        InstallOptions? Options,
        string? SelectedSourcePath);

    private static string SanitizeFolderName(string value)
    {
        value = value.Replace(":", " - ");

        char[] invalid = Path.GetInvalidFileNameChars()
            .Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*'])
            .Distinct()
            .ToArray();

        string result = new string(value
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim()
            .TrimEnd('.');

        if (result.Length > 150)
            result = result[..150].TrimEnd(' ', '.');

        string deviceName = result.Split('.')[0].TrimEnd(' ', '.');

        string[] reserved =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5",
            "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
            "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        if (reserved.Contains(
                deviceName,
                StringComparer.OrdinalIgnoreCase))
        {
            result = "_" + result;
        }

        return result;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static void ShowMessage(
        string message,
        string caption,
        MessageBoxIcon icon)
    {
        OnStaThread(() =>
            MessageBox.Show(
                message,
                caption,
                MessageBoxButtons.OK,
                icon));
    }

    private static T OnStaThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new InvalidOperationException(
                "A Windows dialog operation failed.",
                error);
        }

        return result;
    }
}
