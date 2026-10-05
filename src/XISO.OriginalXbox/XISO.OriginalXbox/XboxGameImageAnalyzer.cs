using XISO.Core.Models;

namespace XISO.OriginalXbox;

public sealed class XboxGameImageAnalyzer
{
    public GameImageInfo Analyze(string isoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);

        if (!File.Exists(isoPath))
        {
            throw new FileNotFoundException(
                "The specified ISO file was not found.",
                isoPath);
        }

        using var reader = new XboxIsoReader(isoPath);

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

            result.Version =
                executionId.Version.ToString();
        }

        return result;
    }
}