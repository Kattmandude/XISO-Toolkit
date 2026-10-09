
using XISO.Core.Data;
using XISO.Core.Metadata;
using XISO.Core.Models;
using XISO.OriginalXbox.Xdvdfs;

namespace XISO.OriginalXbox;

public sealed class XboxGameImageAnalyzer
{
    private readonly IGameTitleProvider _titleProvider;
    private readonly OriginalXboxReleaseDatabase _releaseDatabase;

    public XboxGameImageAnalyzer()
    {
        string dataPath = Path.Combine(AppContext.BaseDirectory, "Data");

        _titleProvider = new LocalGameTitleProvider(
            Path.Combine(dataPath, "GameTitles.json"));

        _releaseDatabase = new OriginalXboxReleaseDatabase(
            Path.Combine(dataPath, "XISO-OriginalXbox-Releases.db"));
    }

    public XboxGameImageAnalyzer(IGameTitleProvider titleProvider)
    {
        ArgumentNullException.ThrowIfNull(titleProvider);

        _titleProvider = titleProvider;

        _releaseDatabase = new OriginalXboxReleaseDatabase(
            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "XISO-OriginalXbox-Releases.db"));
    }

    /// <summary>
    /// Analyzes an ISO/XISO stored in a regular file.
    /// </summary>
    public GameImageInfo Analyze(string isoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);

        if (!File.Exists(isoPath))
        {
            throw new FileNotFoundException(
                "The specified ISO file was not found.",
                isoPath);
        }

        using var stream = new FileStream(
            isoPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return Analyze(
            stream,
            isoPath,
            stream.Length);
    }

    /// <summary>
    /// Analyzes an ISO/XISO from a readable, seekable stream.
    /// The caller retains ownership of the stream.
    /// </summary>
    public GameImageInfo Analyze(
        Stream imageStream,
        string imageName,
        long imageLength)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        if (!imageStream.CanRead || !imageStream.CanSeek)
        {
            throw new ArgumentException(
                "The image stream must support reading and seeking.",
                nameof(imageStream));
        }

        if (imageLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageLength),
                "The image length cannot be negative.");
        }

        imageStream.Position = 0;

        using var reader = new XboxIsoReader(
            imageStream,
            imageName,
            leaveOpen: true);

        var executable = reader.FindDefaultExecutable();

        if (executable is null)
        {
            throw new InvalidDataException(
                "The ISO does not contain a default Xbox executable.");
        }

        var platform = reader.DetectPlatform();

        var result = new GameImageInfo
        {
            FilePath = imageName,
            FileName = Path.GetFileName(imageName),
            Platform = platform,
            Executable = executable.Name,
            FileSize = imageLength
        };

        if (string.Equals(
            executable.Name,
            "default.xex",
            StringComparison.OrdinalIgnoreCase))
        {
            AnalyzeXex(reader, executable, result);
        }
        else if (string.Equals(
            executable.Name,
            "default.xbe",
            StringComparison.OrdinalIgnoreCase))
        {
            AnalyzeXbe(reader, executable, result);
        }

        return result;
    }

    private void AnalyzeXex(
        XboxIsoReader reader,
        XdvdfsDirectoryEntry executable,
        GameImageInfo result)
    {
        var xexData = reader.ReadFile(executable);
        var xexReader = new Xex2Reader();
        var executionId = xexReader.ReadExecutionId(xexData);

        result.TitleId = executionId.TitleId.ToString("X8");
        result.MediaId = executionId.MediaId.ToString("X8");
        result.Version = executionId.Version.ToString();

        var lookup = _titleProvider.Find(
            result.Platform,
            result.TitleId,
            result.MediaId);

        if (lookup is not null)
        {
            result.Title = lookup.Title;
            result.ReleaseRegion = lookup.Region;
        }
    }

    private void AnalyzeXbe(
        XboxIsoReader reader,
        XdvdfsDirectoryEntry executable,
        GameImageInfo result)
    {
        var xbeData = reader.ReadFile(executable);
        var xbeReader = new XbeReader(xbeData);

        result.TitleId = xbeReader.TitleId.ToString("X8");
        result.XbeRegionMask = xbeReader.GameRegion;
        result.XbeRegion = xbeReader.GameRegionDescription;
        result.AlternateTitleIds = xbeReader.AlternateTitleIds;
        result.Title = xbeReader.TitleName;
        result.SerialNumber = xbeReader.SerialNumber;
        result.Xmid = xbeReader.Xmid;
        result.XbeMd5 = xbeReader.Md5;
        result.XbeSizeOfHeaders = xbeReader.SizeOfHeaders;
        result.XbeSizeOfImage = xbeReader.SizeOfImage;
        result.XbeTimeDate = xbeReader.TimeDate;
        result.XbeNumberOfSections = xbeReader.NumberOfSections;
        result.XbeInitFlags = xbeReader.InitFlags;
        result.XbeLibraryVersionCount = xbeReader.LibraryVersionCount;
        result.XbeLibraryVersionsAddress = xbeReader.LibraryVersionsAddress;
        result.XbeLibraryVersions = xbeReader.LibraryVersions;
        result.AllowedMedia = xbeReader.AllowedMedia;
        result.Version = $"0x{xbeReader.Version:X8}";

        var release = _releaseDatabase.FindByXbeMd5(xbeReader.Md5);

        if (release is not null)
        {
            result.ReleaseRegion = release.Region;
        }

        if (string.Equals(
            xbeReader.TitleName,
            "CDX",
            StringComparison.OrdinalIgnoreCase))
        {
            AnalyzeCdx(reader, result);
        }
    }

    private void AnalyzeCdx(
        XboxIsoReader reader,
        GameImageInfo result)
    {
        var files = reader.EnumerateFiles();

        var cdxInx = files.FirstOrDefault(file =>
            file.RelativePath.Equals(
                "cdxmedia\\cdx.inx",
                StringComparison.OrdinalIgnoreCase));

        if (cdxInx is null)
        {
            return;
        }

        var cdxData = reader.ReadFile(cdxInx.Entry);
        var cdxDefinition = CdxDefinitionParser.Parse(cdxData);
        var cdxGames = new List<CdxGameInfo>();

        foreach (var menuItem in cdxDefinition.MenuItems)
        {
            var normalizedFileName = menuItem.FileName.Replace('/', '\\');

            var gameFile = files.FirstOrDefault(file =>
                file.RelativePath.Equals(
                    normalizedFileName,
                    StringComparison.OrdinalIgnoreCase));

            if (gameFile is null)
            {
                continue;
            }

            var gameXbeData = reader.ReadFile(gameFile.Entry);
            var gameXbe = new XbeReader(gameXbeData);

            var gameInfo = new CdxGameInfo
            {
                DisplayName = menuItem.DisplayName,
                FileName = gameFile.RelativePath,
                Title = gameXbe.TitleName,
                TitleId = gameXbe.TitleId.ToString("X8"),
                SerialNumber = gameXbe.SerialNumber,
                Xmid = gameXbe.Xmid,
                XbeMd5 = gameXbe.Md5,
                XbeRegionMask = gameXbe.GameRegion,
                XbeRegion = gameXbe.GameRegionDescription,
                Version = $"0x{gameXbe.Version:X8}",
                XbeSizeOfHeaders = gameXbe.SizeOfHeaders,
                XbeSizeOfImage = gameXbe.SizeOfImage,
                XbeTimeDate = gameXbe.TimeDate,
                XbeNumberOfSections = gameXbe.NumberOfSections,
                XbeInitFlags = gameXbe.InitFlags,
                XbeLibraryVersionCount = gameXbe.LibraryVersionCount,
                XbeLibraryVersionsAddress = gameXbe.LibraryVersionsAddress,
                XbeLibraryVersions = gameXbe.LibraryVersions
            };

            var gameRelease = _releaseDatabase.FindByXbeMd5(gameXbe.Md5);

            if (gameRelease is not null)
            {
                gameInfo.ReleaseRegion = gameRelease.Region;
            }

            cdxGames.Add(gameInfo);
        }

        result.CdxGames = cdxGames;
    }
}