using XISO.Core.Data;
using XISO.Core.Installation;
using XISO.Core.Metadata;
using XISO.Core.Models;

namespace XISO.Xbox360.Installation;

public sealed class GameLibraryScanner : IGameLibraryScanner
{
    public IReadOnlyList<InstalledGameRecord> Scan(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        if (!Directory.Exists(libraryPath))
        {
            throw new DirectoryNotFoundException(
                $"The specified game library was not found: {libraryPath}");
        }

        var games = new List<InstalledGameRecord>();

        string databasePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "GameTitles.json");

        var titleDatabase =
            new GameTitleDatabase(databasePath);

        var xexReader =
            new Xex2Reader();

        foreach (string directory in Directory.EnumerateDirectories(libraryPath))
        {
            string executablePath =
                Path.Combine(
                    directory,
                    "default.xex");

            if (!File.Exists(executablePath))
                continue;

            byte[] xexData =
                File.ReadAllBytes(executablePath);

            var executionId =
                xexReader.ReadExecutionId(xexData);

            string titleId =
                executionId.TitleId.ToString("X8");

            string mediaId =
                executionId.MediaId.ToString("X8");

            var titleRecord =
                titleDatabase.Find(
                    XISO.Core.Detection.XboxPlatform.Xbox360,
                    titleId);

            games.Add(
                new InstalledGameRecord
                {
                    InstallationPath = Path.GetFullPath(directory),
                    FolderName = Path.GetFileName(directory),
                    Platform = XISO.Core.Detection.XboxPlatform.Xbox360,
                    TitleId = titleId,
                    MediaId = mediaId,
                    Title = titleRecord?.Title ?? string.Empty,
                    Executable = "default.xex"
                });
        }

        return games;
    }
}