
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XISO.Core.Installation;
using XISO.Core.Models;

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

        string destination = Path.GetFullPath(
            request.DestinationFolder);

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
        string executable = FindExtractXiso();
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
                "Extracting game contents",
                "extract-xiso is extracting files into a temporary staging folder.",
                null);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                }
            };

            process.StartInfo.ArgumentList.Add("-x");
            process.StartInfo.ArgumentList.Add("-d");
            process.StartInfo.ArgumentList.Add(staging);
            process.StartInfo.ArgumentList.Add(imagePath);

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Could not start extract-xiso.exe.");
            }

            // Drain both redirected streams while the process runs so
            // a full output pipe cannot block the extraction process.
            Task<string> stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            Task<string> stderrTask =
                process.StandardError.ReadToEndAsync();

            try
            {
                while (!process.WaitForExit(200))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException)
                        {
                            // The process may have exited just before Kill.
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Continue to wait and clean up if it has exited.
                        }

                        process.WaitForExit();

                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                // Ensure process output has been fully collected.
                Task.WaitAll(stdoutTask, stderrTask);
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new InvalidDataException(
                    $"extract-xiso failed with exit code {process.ExitCode}." +
                    FormatProcessOutput(stdout, stderr));
            }

            if (!Directory.EnumerateFileSystemEntries(
                    staging, "*", SearchOption.AllDirectories).Any())
            {
                throw new InvalidDataException(
                    "extract-xiso reported success, but extracted no files." +
                    FormatProcessOutput(stdout, stderr));
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
                100);

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
            return 100;

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

    private static string FindExtractXiso()
    {
        string relativePath =
            Path.Combine("tools", "extract-xiso", "extract-xiso.exe");

        string? directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(directory, relativePath);

            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        directory = Environment.CurrentDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            string candidate = Path.Combine(directory, relativePath);

            if (File.Exists(candidate))
                return candidate;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException(
            "Could not locate tools\\extract-xiso\\extract-xiso.exe. " +
            "Expected it under the XISO Toolkit solution or application directory.");
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

    private static string FormatProcessOutput(
        string stdout,
        string stderr)
    {
        var result = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(stderr))
            result.AppendLine().AppendLine(stderr.Trim());

        if (!string.IsNullOrWhiteSpace(stdout))
            result.AppendLine().AppendLine(stdout.Trim());

        return result.ToString();
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
}