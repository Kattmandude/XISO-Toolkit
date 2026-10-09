
using System;
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
    public static async Task RunAsync(ToolkitConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? selectedSourcePath = OnStaThread(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select an Xbox game ISO",
                Filter = "Xbox images and archives (*.iso;*.xiso;*.zip;*.7z;*.rar)|*.iso;*.xiso;*.zip;*.7z;*.rar|Disc images (*.iso;*.xiso)|*.iso;*.xiso|Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.FileName
                : null;
        });

        if (string.IsNullOrWhiteSpace(selectedSourcePath))
            return;

        ArchiveImageSource archiveSource;

        try
        {
            archiveSource = ArchiveImageSource.Open(selectedSourcePath);
        }
        catch (Exception ex)
        {
            ShowMessage(
                $"The selected file could not be opened as an Xbox image or supported archive.\n\n{ex.Message}",
                "Installation",
                MessageBoxIcon.Error);
            return;
        }

        using var archiveSourceLifetime = archiveSource;

        GameImageInfo game;

        try
        {
            Console.WriteLine($"Analyzing: {archiveSource.ImageName}");

            game = new XboxGameImageAnalyzer().Analyze(
                archiveSource.ImageStream,
                archiveSource.ImageName,
                archiveSource.ImageLength);
        }
        catch (Exception ex)
        {
            ShowMessage(
                $"The ISO could not be analyzed.\n\n{ex.Message}",
                "Installation",
                MessageBoxIcon.Error);
            return;
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
                Console.WriteLine($"MobCat lookup unavailable: {ex.Message}");
            }
        }

        DBoxDisc? dboxDisc = null;

        if (!string.IsNullOrWhiteSpace(game.Xmid))
        {
            try
            {
                dboxDisc = await new DBoxClient()
                    .GetDiscByXmidAsync(game.Xmid);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DBox lookup unavailable: {ex.Message}");
            }
        }

        string title = FirstNonEmpty(
            mobCatTitle?.FullName,
            mobCatTitle?.TitleName,
            game.Title,
            Path.GetFileNameWithoutExtension(archiveSource.ImageName));

        GameLibrary? library = configuration.GameLibraries
            .FirstOrDefault(item => item.Platform == game.Platform);

        string defaultRoot = library?.Path ?? string.Empty;

        InstallOptions? options = ShowInstallOptions(
            title,
            archiveSource.ImageName,
            mobCatTitle?.Region,
            dboxDisc?.RedumpRegion,
            defaultRoot);

        if (options is null)
            return;

        bool extractFiles =
            options.Method == InstallationMethod.ExtractGameFiles;

        bool createGameFolder =
            extractFiles || options.CreateGameFolderForImage;

        string folderName = SanitizeFolderName(options.Name);

        if (createGameFolder && string.IsNullOrWhiteSpace(folderName))
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
            IsoPath = archiveSource.ImagePath,
            DestinationFolder = destinationFolder,
            Method = options.Method,
            IsoFileName = extractFiles
                ? archiveSource.ImageName
                : imageFileName,
            CreateGameFolderForImage = createGameFolder
        };

        InstallationProgressDialog.Show(
            title,
            $"{methodDescription} ({game.Platform})",
            selectedSourcePath,
            outputPath,
            (progress, cancellationToken) =>
                installer.Install(
                    archiveSource.ImageStream,
                    archiveSource.ImageName,
                    request,
                    progress,
                    cancellationToken));
    }

    private static InstallOptions? ShowInstallOptions(
        string title,
        string imageName,
        string? region,
        string? releaseCountry,
        string defaultRoot)
    {
        return OnStaThread(() =>
        {
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

            using var form = new Form
            {
                Text = "Install Game",
                Width = 700,
                Height = 700,
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
                Width = 200,
                Checked = true
            };

            var regionRadio = new RadioButton
            {
                Text = usableRegion is null
                    ? "MobCat region (unavailable)"
                    : $"MobCat region: {usableRegion}",
                Left = 15,
                Top = 47,
                Width = 600,
                Enabled = usableRegion is not null
            };

            var countryRadio = new RadioButton
            {
                Text = usableCountry is null
                    ? "DBox release country (unavailable)"
                    : $"DBox release country: {usableCountry}",
                Left = 15,
                Top = 72,
                Width = 600,
                Enabled = usableCountry is not null
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
                regionRadio,
                countryRadio,
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

            string lastGeneratedImageName = originalImageName;
            string? lastAppliedImageSuffix = null;


            void RefreshImageNameSuffix()
            {
                if (!applySuffixToImageCheckBox.Checked)
                {
                    lastAppliedImageSuffix = null;
                    lastGeneratedImageName = originalImageName;
                    imageNameBox.Text = originalImageName;
                    return;
                }

                // Use the exact same title and suffix logic as the game folder name.
                string gameName = MakeAutomaticName();

                // Preserve the original image extension, including compound extensions
                // such as .xiso.iso.
                string extension = originalImageName.EndsWith(
                        ".xiso.iso",
                        StringComparison.OrdinalIgnoreCase)
                    ? originalImageName[^9..]
                    : Path.GetExtension(originalImageName);

                string generatedFileName = gameName + extension;

                lastAppliedImageSuffix = GetSelectedSuffix();
                lastGeneratedImageName = generatedFileName;
                imageNameBox.Text = generatedFileName;
            }

            void RefreshFolderName()
            {
                bool extractFiles = extractRadio.Checked;
                bool createFolder = gameFolderRadio.Checked;

                // Extraction always needs a folder. For image mode,
                // the folder name is irrelevant when copying to the root.
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
                    (regionRadio.Checked || countryRadio.Checked);

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

            fullCountryRadio.CheckedChanged += (_, _) =>
            {
                if (fullCountryRadio.Checked &&
                    (countryRadio.Checked || regionRadio.Checked))
                {
                    RefreshNamingOptions();
                }
            };

            countryCodeRadio.CheckedChanged += (_, _) =>
            {
                if (countryCodeRadio.Checked &&
                    (countryRadio.Checked || regionRadio.Checked))
                {
                    RefreshNamingOptions();
                }
            };

            applySuffixToImageCheckBox.CheckedChanged += (_, _) =>
            {
                if (applySuffixToImageCheckBox.Checked)
                {
                    // Start with the original image name when enabling
                    // suffix application for the first time.
                    imageNameBox.Text = originalImageName;
                }

                RefreshImageNameSuffix();
            };

            imageNameBox.TextChanged += (_, _) =>
            {
                // If the user edits the image filename manually while
                // suffix application is disabled, leave it independent.
                if (!applySuffixToImageCheckBox.Checked)
                    lastGeneratedImageName = imageNameBox.Text;
            };

            extractRadio.CheckedChanged += (_, _) =>
                RefreshImageOptions();

            keepImageRadio.CheckedChanged += (_, _) =>
                RefreshImageOptions();

            libraryRootRadio.CheckedChanged += (_, _) =>
                RefreshFolderName();

            gameFolderRadio.CheckedChanged += (_, _) =>
                RefreshFolderName();

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

            var resetButton = new Button
            {
                Text = "Reset Folder Name",
                Left = 15,
                Top = 565,
                Width = 145
            };

            resetButton.Click += (_, _) =>
            {
                nameBox.Text = MakeAutomaticName();
            };

            var resetImageNameButton = new Button
            {
                Text = "Reset ISO/XISO Filename",
                Left = 170,
                Top = 565,
                Width = 190
            };

            resetImageNameButton.Click += (_, _) =>
            {
                imageNameBox.Text = originalImageName;

                if (applySuffixToImageCheckBox.Checked)
                    RefreshImageNameSuffix();
            };

            var installButton = new Button
            {
                Text = "Install",
                Left = 450,
                Top = 565,
                Width = 100,
                DialogResult = DialogResult.OK
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                Left = 560,
                Top = 565,
                Width = 105,
                DialogResult = DialogResult.Cancel
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
                resetButton,
                resetImageNameButton,
                installButton,
                cancelButton
            ]);

            form.AcceptButton = installButton;
            form.CancelButton = cancelButton;

            if (form.ShowDialog() != DialogResult.OK)
                return null;

            string selectedName = nameBox.Text.Trim();
            string selectedRoot = rootBox.Text.Trim();
            string selectedImageName = imageNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(selectedRoot))
                return null;

            InstallationMethod method = extractRadio.Checked
                ? InstallationMethod.ExtractGameFiles
                : InstallationMethod.MoveIsoImage;

            return new InstallOptions(
                selectedName,
                selectedRoot,
                method,
                selectedImageName,
                gameFolderRadio.Checked);
        });
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

            parts[i] = string.Equals(
                    item,
                    "Europe",
                    StringComparison.OrdinalIgnoreCase)
                ? "EU"
                : GetCountryCodeOrOriginal(item);
        }

        return string.Join(
            ", ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string GetCountryCodeOrOriginal(string country)
    {
        string cleaned = country.Trim();

        var aliases =
            new System.Collections.Generic.Dictionary<string, string>(
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
        bool CreateGameFolderForImage);

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