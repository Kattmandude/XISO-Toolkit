using XISO.Core.Data;
using XISO.Core.Metadata;
using XISO.Core.Models;

namespace XISO.OriginalXbox;

public sealed class XboxGameImageAnalyzer
{
    private readonly IGameTitleProvider _titleProvider;
    private readonly OriginalXboxReleaseDatabase _releaseDatabase;

    public XboxGameImageAnalyzer()
    {
        string dataPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Data");

        _titleProvider =
            new LocalGameTitleProvider(
                Path.Combine(
                    dataPath,
                    "GameTitles.json"));

        _releaseDatabase =
            new OriginalXboxReleaseDatabase(
                Path.Combine(
                    dataPath,
                    "XISO-OriginalXbox-Releases.db"));
    }

    public XboxGameImageAnalyzer(
        IGameTitleProvider titleProvider)
    {
        ArgumentNullException.ThrowIfNull(titleProvider);

        _titleProvider = titleProvider;

        _releaseDatabase =
            new OriginalXboxReleaseDatabase(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Data",
                    "XISO-OriginalXbox-Releases.db"));
    }

    public GameImageInfo Analyze(string isoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);

        if (!File.Exists(isoPath))
        {
            throw new FileNotFoundException(
                "The specified ISO file was not found.",
                isoPath);
        }

        using var reader =
            new XboxIsoReader(isoPath);

        var executable =
            reader.FindDefaultExecutable();

        if (executable is null)
        {
            throw new InvalidDataException(
                "The ISO does not contain a default Xbox executable.");
        }

        var platform =
            reader.DetectPlatform();

        var result =
            new GameImageInfo
            {
                FilePath = isoPath,
                FileName = Path.GetFileName(isoPath),
                Platform = platform,
                Executable = executable.Name,
                FileSize = new FileInfo(isoPath).Length
            };

        if (string.Equals(
            executable.Name,
            "default.xex",
            StringComparison.OrdinalIgnoreCase))
        {
            var xexData =
                reader.ReadFile(executable);

            var xexReader =
                new Xex2Reader();

            var executionId =
                xexReader.ReadExecutionId(xexData);

            result.TitleId =
                executionId.TitleId.ToString("X8");

            result.MediaId =
                executionId.MediaId.ToString("X8");

            result.Version =
                executionId.Version.ToString();

            var lookup =
                _titleProvider.Find(
                    result.Platform,
                    result.TitleId,
                    result.MediaId);

            if (lookup is not null)
            {
                result.Title =
                    lookup.Title;

                result.ReleaseRegion =
                    lookup.Region;
            }
        }
        else if (string.Equals(
            executable.Name,
            "default.xbe",
            StringComparison.OrdinalIgnoreCase))
        {
            var xbeData =
                reader.ReadFile(executable);

            var xbeReader =
                new XbeReader(xbeData);

            result.TitleId =
                xbeReader.TitleId.ToString("X8");

            result.XbeRegionMask =
                xbeReader.GameRegion;

            result.AlternateTitleIds =
                xbeReader.AlternateTitleIds;

            result.Title =
                xbeReader.TitleName;

            result.SerialNumber =
                xbeReader.SerialNumber;

            result.Xmid =
                xbeReader.Xmid;

            result.Version =
                $"0x{xbeReader.Version:X8}";

            var release =
                _releaseDatabase.FindByXbeMd5(
                    xbeReader.Md5);

            if (release is not null)
            {
                result.ReleaseRegion =
                    release.Region;
            }
        }

        return result;
    }
}