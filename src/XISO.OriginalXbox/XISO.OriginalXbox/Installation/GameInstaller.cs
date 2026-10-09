
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using XISO.Core.Installation;
using XISO.Core.Models;
using XISOSharp;
using XISOSharp.Models;

namespace XISO.OriginalXbox.Installation;

public sealed class GameInstaller : IGameInstaller
{
    private const int CopyBufferSize = 1024 * 1024;

    public string Install(GameInstallationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (!File.Exists(request.IsoPath))
        {
            throw new FileNotFoundException(
                "The specified ISO/XISO image was not found.",
                request.IsoPath);
        }

        return InstallFromFile(
            request.IsoPath,
            request,
            progress: null,
            cancellationToken: CancellationToken.None);
    }

    public string Install(
        Stream imageStream,
        string imageName,
        GameInstallationRequest request,
        IProgress<InstallationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (!imageStream.CanRead || !imageStream.CanSeek)
        {
            throw new ArgumentException(
                "The image stream must support reading and seeking.",
                nameof(imageStream));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"xiso-install-{Guid.NewGuid():N}");

        Directory.CreateDirectory(temporaryRoot);

        try
        {
            string temporaryImage = Path.Combine(
                temporaryRoot,
                GetSafeImageFileName(imageName));

            Report(
                progress,
                "Preparing image",
                "Reading the ISO/XISO image. If the source is an archive, this also drives its decompression.",
                0);

            imageStream.Position = 0;

            CopyStreamWithProgress(
                imageStream,
                temporaryImage,
                imageStream.Length,
                "Preparing image",
                "Extracting image data from the source",
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                "Preparing image",
                "Image data is ready. Starting the selected installation operation.",
                100);

            return InstallFromFile(
                temporaryImage,
                request,
                progress,
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    private static void ValidateRequest(GameInstallationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationFolder))
        {
            throw new ArgumentException(
                "Destination folder cannot be empty.",
                nameof(request));
        }

        if (request.Method is not InstallationMethod.ExtractGameFiles
            and not InstallationMethod.MoveIsoImage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.Method),
                request.Method,
                "Unsupported installation method.");
        }

        if (request.Method == InstallationMethod.MoveIsoImage)
        {
            string fileName = GetSafeImageFileName(request.IsoFileName);

            if (string.IsNullOrWhiteSpace(request.IsoFileName) ||
                fileName != request.IsoFileName.Trim())
            {
                throw new ArgumentException(
                    "Enter a valid ISO/XISO filename without a directory path.",
                    nameof(request.IsoFileName));
            }
        }
    }

    private static string InstallFromFile(
        string imagePath,
        GameInstallationRequest request,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string destination = Path.GetFullPath(request.DestinationFolder);
        string? parent = Path.GetDirectoryName(destination);

        if (request.Method == InstallationMethod.MoveIsoImage)
        {
            return request.CreateGameFolderForImage
                ? KeepImageInNewFolder(
                    imagePath,
                    destination,
                    request.IsoFileName,
                    progress,
                    cancellationToken)
                : KeepImageInLibraryRoot(
                    imagePath,
                    destination,
                    request.IsoFileName,
                    progress,
                    cancellationToken);
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException(
                $"The destination already exists. Nothing was overwritten: {destination}");
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"The destination's parent folder does not exist: {parent}");
        }

        return ExtractGameFiles(
            imagePath,
            destination,
            progress,
            cancellationToken);
    }

    private static string ExtractGameFiles(
        string imagePath,
        string destination,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string parent = Path.GetDirectoryName(destination)!;

        string staging = Path.Combine(
            parent,
            $".xiso-staging-{Guid.NewGuid():N}");

        Directory.CreateDirectory(staging);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                "Analyzing game contents",
                "Counting files and calculating the total extraction size.",
                0);

            long totalBytes;

            using (var imageStream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.RandomAccess))
            {
                totalBytes = GetTotalFileBytes(
                    imageStream,
                    imagePath,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                "Extracting game contents",
                $"Extracting {FormatBytes(totalBytes)} of game data.",
                0);

            var byteCountsByPath = new Dictionary<string, long>(
                StringComparer.OrdinalIgnoreCase);

            long completedBytes = 0;

            var extractionProgress = new InlineProgress<ProgressInfo>(
                info =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (info.Type is not ProgressInfoType.FileProgress
                        and not ProgressInfoType.FileAdded)
                    {
                        return;
                    }

                    string path = info.Path ?? string.Empty;

                    long reportedBytes = info.Size;

                    if (reportedBytes < 0)
                    {
                        reportedBytes = 0;
                    }

                    byteCountsByPath.TryGetValue(
                        path,
                        out long previousBytes);

                    long newBytes = Math.Max(previousBytes, reportedBytes);

                    if (newBytes > previousBytes)
                    {
                        completedBytes += newBytes - previousBytes;
                        byteCountsByPath[path] = newBytes;
                    }

                    int percent = GetPercent(completedBytes, totalBytes);

                    Report(
                        progress,
                        "Extracting game contents",
                        string.IsNullOrWhiteSpace(path)
                            ? $"Extracted {FormatBytes(completedBytes)} of {FormatBytes(totalBytes)}"
                            : $"Extracting {path} — {FormatBytes(completedBytes)} of {FormatBytes(totalBytes)}",
                        Math.Min(percent, 99));
                });

            int result;

            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;

            try
            {
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);

                using (var imageStream = new FileStream(
                    imagePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    FileOptions.RandomAccess))
                {
                    result = XisoReader.UnpackImage(
                        imageStream,
                        Path.GetFileName(imagePath),
                        staging,
                        cancellationToken,
                        null,
                        null,
                        extractionProgress);
                }
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (result != 0)
            {
                throw new InvalidDataException(
                    $"XISOSharp failed to extract the image (return code {result}).");
            }

            if (!Directory.EnumerateFileSystemEntries(
                    staging, "*", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException(
                    "XISOSharp reported success, but extracted no files.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new IOException(
                    $"The destination appeared during extraction: {destination}");
            }

            Report(
                progress,
                "Finalizing installation",
                "Extraction completed. Moving the staged game files into the destination.",
                99);

            Directory.Move(staging, destination);

            Report(
                progress,
                "Completed",
                "Game contents were installed successfully.",
                100);

            return destination;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static long GetTotalFileBytes(
        Stream imageStream,
        string imageName,
        CancellationToken cancellationToken)
    {
        long totalBytes = 0;

        void Walk(string directory)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (EntryInfo entry in XisoReader.ListDirectory(
                imageStream,
                imageName,
                directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string path = directory == "/"
                    ? "/" + entry.Name
                    : directory + "/" + entry.Name;

                if (entry.IsDirectory)
                {
                    Walk(path);
                }
                else
                {
                    totalBytes += entry.FileSize;
                }
            }
        }

        imageStream.Position = 0;
        Walk("/");
        imageStream.Position = 0;

        return totalBytes;
    }

    private static string KeepImageInNewFolder(
        string imagePath,
        string destination,
        string requestedFileName,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string? parent = Path.GetDirectoryName(destination);

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"The destination's parent folder does not exist: {parent}");
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException(
                $"The destination already exists. Nothing was overwritten: {destination}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(destination);

        try
        {
            string outputPath = Path.Combine(
                destination,
                GetSafeImageFileName(requestedFileName));

            CopyFileWithProgress(
                imagePath,
                outputPath,
                "Copying ISO/XISO",
                "Copying the image into its destination folder",
                progress,
                cancellationToken);

            Report(
                progress,
                "Completed",
                "ISO/XISO image copied successfully.",
                100);

            return outputPath;
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    private static string KeepImageInLibraryRoot(
        string imagePath,
        string destination,
        string requestedFileName,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(destination))
        {
            throw new DirectoryNotFoundException(
                $"The game library folder does not exist: {destination}");
        }

        string outputPath = Path.Combine(
            destination,
            GetSafeImageFileName(requestedFileName));

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException(
                $"The image destination already exists. Nothing was overwritten: {outputPath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        CopyFileWithProgress(
            imagePath,
            outputPath,
            "Copying ISO/XISO",
            "Copying the image directly into the game library folder",
            progress,
            cancellationToken);

        Report(
            progress,
            "Completed",
            "ISO/XISO image copied successfully.",
            100);

        return outputPath;
    }

    private static void CopyStreamWithProgress(
        Stream input,
        string outputPath,
        long totalBytes,
        string stage,
        string message,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        long copied = 0;
        byte[] buffer = new byte[CopyBufferSize];

        try
        {
            using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = input.Read(buffer, 0, buffer.Length);

                if (read == 0)
                    break;

                output.Write(buffer, 0, read);
                copied += read;

                Report(
                    progress,
                    stage,
                    $"{message}: {FormatBytes(copied)} of {FormatBytes(totalBytes)}",
                    GetPercent(copied, totalBytes));
            }

            output.Flush();
        }
        catch
        {
            TryDeleteFile(outputPath);
            throw;
        }
    }

    private static void CopyFileWithProgress(
        string sourcePath,
        string destinationPath,
        string stage,
        string message,
        IProgress<InstallationProgress>? progress,
        CancellationToken cancellationToken)
    {
        long totalBytes = new FileInfo(sourcePath).Length;

        using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.SequentialScan);

        CopyStreamWithProgress(
            input,
            destinationPath,
            totalBytes,
            stage,
            message,
            progress,
            cancellationToken);
    }

    private static int GetPercent(long completed, long total)
    {
        if (total <= 0)
            return 0;

        return (int)Math.Clamp(
            completed * 100.0 / total,
            0,
            100);
    }

    private static string FormatBytes(long bytes)
    {
        const double MiB = 1024.0 * 1024.0;
        const double GiB = 1024.0 * 1024.0 * 1024.0;

        return bytes >= GiB
            ? $"{bytes / GiB:F2} GiB"
            : $"{bytes / MiB:F1} MiB";
    }

    private static void Report(
        IProgress<InstallationProgress>? progress,
        string stage,
        string message,
        int? percent)
    {
        progress?.Report(new InstallationProgress(
            stage,
            message,
            percent));
    }

    private static string GetSafeImageFileName(string imageName)
    {
        string name = Path.GetFileName(imageName);

        if (string.IsNullOrWhiteSpace(name) ||
            (!name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) &&
             !name.EndsWith(".xiso", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The filename must end in .iso or .xiso.",
                nameof(imageName));
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Contains('/') ||
            name.Contains('\\'))
        {
            throw new ArgumentException(
                "The filename contains invalid characters.",
                nameof(imageName));
        }

        return name;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; do not mask the original exception.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; do not mask the original exception.
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }
}