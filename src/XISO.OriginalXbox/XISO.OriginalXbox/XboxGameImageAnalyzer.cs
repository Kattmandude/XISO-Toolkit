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

        return Analyze(stream, isoPath, stream.Length);
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
        byte[] xexData = reader.ReadFile(executable);

        var xexReader = new Xex2Reader();
        var executionId = xexReader.ReadExecutionId(xexData);

        // Preserve the XEX execution metadata.
        result.TitleId = executionId.TitleId.ToString("X8");
        result.MediaId = executionId.MediaId.ToString("X8");

        result.XexVersion = executionId.Version;
        result.XexBaseVersion = executionId.BaseVersion;
        result.XexPlatform = executionId.Platform;
        result.XexExecutableType = executionId.ExecutableType;
        result.XexDiscNumber = executionId.DiscNumber;
        result.XexDiscCount = executionId.DiscCount;
        result.XexSaveGameId = executionId.SaveGameId;

        // Read and preserve the XEX security metadata.
        // Do this before reconstruction so region data remains
        // available even if the compression format is unsupported.
        var security = xexReader.ReadSecurityInfo(xexData);

        result.XexRegionMask = security.GameRegion;
        result.XexRegion =
            Xex2RegionDecoder.Decode(security.GameRegion);

        // Recover the title from the embedded XDBF resource.
        byte[] image;

        try
        {
            var reconstructor = new Xex2ImageReconstructor();
            image = reconstructor.Reconstruct(xexData);
        }
        catch (NotSupportedException)
        {
            // Unsupported XEX formats must not prevent basic metadata
            // from being reported.
            return;
        }

        var resourceReader = new Xex2ResourceInfoReader();
        var resources = resourceReader.ReadResources(xexData);

        var titleResource = resources.FirstOrDefault(resource =>
            string.Equals(
                resource.Name,
                result.TitleId,
                StringComparison.OrdinalIgnoreCase));

        if (titleResource is null)
        {
            return;
        }

        long resourceOffset =
            (long)titleResource.Address - security.LoadAddress;

        if (titleResource.Size > int.MaxValue ||
            resourceOffset < 0 ||
            resourceOffset + titleResource.Size > image.Length)
        {
            throw new InvalidDataException(
                $"XEX title resource '{titleResource.Name}' " +
                "extends beyond the reconstructed image.");
        }

        byte[] resourceData = image.AsSpan(
            (int)resourceOffset,
            (int)titleResource.Size).ToArray();

        var titleReader = new XdbfTitleReader();
        string? title = titleReader.ReadTitle(resourceData);

        if (!string.IsNullOrWhiteSpace(title))
        {
            result.Title = title;
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
        // TODO: Restore the original CDX menu analysis implementation.
        // The original method was not present in the supplied source.
    }
}
