using XISO.Core.Installation;
using XISO.Core.Models;

namespace XISO.OriginalXbox.Installation;

public sealed class GameInstaller : IGameInstaller
{
    public string Install(GameInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.IsoPath))
            throw new ArgumentException(
                "ISO path cannot be empty.",
                nameof(request));

        if (!File.Exists(request.IsoPath))
            throw new FileNotFoundException(
                "The specified ISO file was not found.",
                request.IsoPath);

        if (string.IsNullOrWhiteSpace(request.DestinationFolder))
            throw new ArgumentException(
                "Destination folder cannot be empty.",
                nameof(request));

        return request.Method switch
        {
            InstallationMethod.ExtractGameFiles =>
                ExtractGameFiles(request),

            InstallationMethod.MoveIsoImage =>
                MoveIsoImage(request),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(request.Method),
                    request.Method,
                    "Unsupported installation method.")
        };
    }

    private static string ExtractGameFiles(
        GameInstallationRequest request)
    {
        Directory.CreateDirectory(request.DestinationFolder);

        using var reader =
            new XboxIsoReader(request.IsoPath);

        foreach (var file in reader.EnumerateFiles())
        {
            string destinationPath =
                Path.Combine(
                    request.DestinationFolder,
                    file.RelativePath);

            string? destinationDirectory =
                Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"The destination file already exists: {destinationPath}");
            }

            byte[] data =
                reader.ReadFile(file.Entry);

            File.WriteAllBytes(
                destinationPath,
                data);
        }

        return Path.GetFullPath(
            request.DestinationFolder);
    }

    private static string MoveIsoImage(
        GameInstallationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IsoFileName))
            throw new ArgumentException(
                "ISO filename cannot be empty when moving an ISO.",
                nameof(request));

        Directory.CreateDirectory(request.DestinationFolder);

        string destinationPath =
            Path.Combine(
                request.DestinationFolder,
                request.IsoFileName);

        if (File.Exists(destinationPath))
        {
            throw new IOException(
                $"The destination ISO already exists: {destinationPath}");
        }

        File.Move(
            request.IsoPath,
            destinationPath);

        return Path.GetFullPath(destinationPath);
    }
}