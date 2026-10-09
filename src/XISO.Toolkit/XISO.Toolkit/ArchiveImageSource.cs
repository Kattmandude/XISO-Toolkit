
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XISO.Toolkit;

internal sealed class ArchiveImageSource : IDisposable
{
    private readonly Stream _imageStream;
    private readonly bool _ownsStream;

    // Compatibility property for the existing workflow.
    // For archives, this is the archive path, NOT an extracted ISO path.
    public string ImagePath { get; }

    public string ImageName { get; }

    public Stream ImageStream => _imageStream;

    public long ImageLength => _imageStream.Length;

    public bool IsArchive { get; }

    private ArchiveImageSource(
        string sourcePath,
        string imageName,
        Stream imageStream,
        bool isArchive,
        bool ownsStream)
    {
        ImagePath = sourcePath;
        ImageName = imageName;
        _imageStream = imageStream;
        IsArchive = isArchive;
        _ownsStream = ownsStream;
    }

    public static ArchiveImageSource Open(
        string sourcePath,
        int maxBufferedMB = 16384)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (maxBufferedMB < 64 || maxBufferedMB > 16384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBufferedMB),
                "The archive buffer limit must be between 64 and 16384 MB.");
        }

        sourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "The selected image or archive was not found.",
                sourcePath);
        }

        string extension = Path.GetExtension(sourcePath);

        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xiso", StringComparison.OrdinalIgnoreCase))
        {
            var fileStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return new ArchiveImageSource(
                sourcePath,
                Path.GetFileName(sourcePath),
                fileStream,
                isArchive: false,
                ownsStream: true);
        }

        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Unsupported file type '{extension}'. " +
                "Select an ISO, XISO, ZIP, 7z, or RAR file.");
        }

        string sevenZipPath = FindSevenZip();

        List<ArchiveEntry> entries = ListArchiveEntries(
            sevenZipPath,
            sourcePath);

        ArchiveEntry[] candidates = entries
            .Where(entry =>
                !entry.IsDirectory &&
                (Path.GetExtension(entry.Name).Equals(
                     ".iso", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetExtension(entry.Name).Equals(
                     ".xiso", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidDataException(
                "The archive contains no ISO or XISO image.");
        }

        if (candidates.Length > 1)
        {
            string names = string.Join(
                Environment.NewLine,
                candidates.Take(10).Select(entry => $"• {entry.Name}"));

            if (candidates.Length > 10)
            {
                names += Environment.NewLine +
                         $"…and {candidates.Length - 10} more.";
            }

            throw new InvalidDataException(
                $"The archive contains {candidates.Length} ISO/XISO images. " +
                "Please select an archive containing only one game image." +
                Environment.NewLine + Environment.NewLine + names);
        }

        ArchiveEntry candidate = candidates[0];

        if (candidate.Size < 0)
        {
            throw new InvalidDataException(
                $"7-Zip did not report a valid size for '{candidate.Name}'.");
        }

        Console.WriteLine($"Archive: {Path.GetFileName(sourcePath)}");
        Console.WriteLine($"Image entry: {candidate.Name}");
        Console.WriteLine($"Image size: {candidate.Size:N0} bytes");
        Console.WriteLine($"Maximum temporary cache: {maxBufferedMB:N0} MB");

        Stream stream = new SevenZipEntryStream(
            sevenZipPath,
            sourcePath,
            candidate.Name,
            candidate.Size,
            maxBufferedMB);

        return new ArchiveImageSource(
            sourcePath,
            candidate.Name,
            stream,
            isArchive: true,
            ownsStream: true);
    }

    private static string FindSevenZip()
    {
        string? fromPath = FindOnPath("7z.exe");

        if (fromPath is not null)
            return fromPath;

        string? programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);

        string? programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        [
            Path.Combine(programFiles, "7-Zip", "7z.exe"),
            Path.Combine(programFilesX86, "7-Zip", "7z.exe")
        ];

        foreach (string path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            "7-Zip was not found. Install 7-Zip or add 7z.exe to PATH.");
    }

    private static string? FindOnPath(string fileName)
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (string directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim(), fileName);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static List<ArchiveEntry> ListArchiveEntries(
        string sevenZipPath,
        string archivePath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = sevenZipPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("l");
        process.StartInfo.ArgumentList.Add("-slt");
        process.StartInfo.ArgumentList.Add("-sccUTF-8");
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(archivePath);

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Could not start 7-Zip to list the archive.");
        }

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"7-Zip could not list the archive.{Environment.NewLine}" +
                standardError);
        }

        var entries = new List<ArchiveEntry>();
        var fields = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        void FlushEntry()
        {
            if (!fields.TryGetValue("Path", out string? name))
            {
                fields.Clear();
                return;
            }

            // The first Path record is the archive itself, not an entry.
            if (!fields.ContainsKey("Size") &&
                !fields.ContainsKey("Folder"))
            {
                fields.Clear();
                return;
            }

            bool isDirectory =
                fields.TryGetValue("Folder", out string? folder) &&
                folder.Equals("+", StringComparison.OrdinalIgnoreCase);

            long size = -1;

            if (fields.TryGetValue("Size", out string? sizeText))
            {
                long.TryParse(
                    sizeText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out size);
            }

            entries.Add(new ArchiveEntry(name, size, isDirectory));
            fields.Clear();
        }

        using var reader = new StringReader(standardOutput);

        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0)
            {
                FlushEntry();
                continue;
            }

            int separator = line.IndexOf(" = ", StringComparison.Ordinal);

            if (separator > 0)
            {
                string key = line[..separator];
                string value = line[(separator + 3)..];
                fields[key] = value;
            }
        }

        FlushEntry();

        return entries;
    }

    public void Dispose()
    {
        if (_ownsStream)
            _imageStream.Dispose();
    }

    private sealed record ArchiveEntry(
        string Name,
        long Size,
        bool IsDirectory);

    private sealed class SevenZipEntryStream : Stream
    {
        private const int BlockSize = 1024 * 1024;

        private readonly Process _process;
        private readonly Stream _output;
        private readonly FileStream _cache;
        private readonly string _cachePath;
        private readonly long _length;
        private readonly long _maximumBufferedBytes;
        private readonly object _sync = new();

        private long _bufferedLength;
        private long _position;
        private bool _eof;
        private bool _disposed;

        public SevenZipEntryStream(
            string sevenZipPath,
            string archivePath,
            string entryName,
            long length,
            int maxBufferedMB)
        {
            _length = length;
            _maximumBufferedBytes = checked(
                (long)maxBufferedMB * 1024 * 1024);

            _cachePath = Path.Combine(
                Path.GetTempPath(),
                $"xiso-archive-cache-{Guid.NewGuid():N}.tmp");

            try
            {
                _cache = new FileStream(
                    _cachePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    BlockSize,
                    FileOptions.RandomAccess | FileOptions.DeleteOnClose);
            }
            catch
            {
                TryDeleteCacheFile();
                throw;
            }

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _process.StartInfo.ArgumentList.Add("x");
            _process.StartInfo.ArgumentList.Add("-so");
            _process.StartInfo.ArgumentList.Add("-y");
            _process.StartInfo.ArgumentList.Add("-bd");
            _process.StartInfo.ArgumentList.Add("-bb0");
            _process.StartInfo.ArgumentList.Add("--");
            _process.StartInfo.ArgumentList.Add(archivePath);
            _process.StartInfo.ArgumentList.Add(entryName);

            try
            {
                if (!_process.Start())
                {
                    throw new InvalidOperationException(
                        "Could not start 7-Zip to stream the image entry.");
                }

                _output = _process.StandardOutput.BaseStream;

                // Drain stderr concurrently so 7-Zip cannot block on a full pipe.
                _ = _process.StandardError.ReadToEndAsync();
            }
            catch
            {
                _cache.Dispose();
                _process.Dispose();
                TryDeleteCacheFile();
                throw;
            }
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if (offset < 0 || count < 0 ||
                offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            if (count == 0 || _position >= _length)
                return 0;

            lock (_sync)
            {
                long requestedEnd = Math.Min(
                    _length,
                    checked(_position + count));

                FillTo(requestedEnd);

                int available = (int)Math.Min(
                    count,
                    _bufferedLength - _position);

                if (available <= 0)
                    return 0;

                _cache.Position = _position;

                int totalRead = 0;

                while (totalRead < available)
                {
                    int read = _cache.Read(
                        buffer,
                        offset + totalRead,
                        available - totalRead);

                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            "The temporary archive cache ended unexpectedly.");
                    }

                    totalRead += read;
                }

                _position += totalRead;
                return totalRead;
            }
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (buffer.IsEmpty || _position >= _length)
                return 0;

            lock (_sync)
            {
                long requestedEnd = Math.Min(
                    _length,
                    checked(_position + buffer.Length));

                FillTo(requestedEnd);

                int available = (int)Math.Min(
                    buffer.Length,
                    _bufferedLength - _position);

                if (available <= 0)
                    return 0;

                _cache.Position = _position;

                int totalRead = 0;

                while (totalRead < available)
                {
                    int read = _cache.Read(
                        buffer.Slice(totalRead, available - totalRead));

                    if (read == 0)
                    {
                        throw new EndOfStreamException(
                            "The temporary archive cache ended unexpectedly.");
                    }

                    totalRead += read;
                }

                _position += totalRead;
                return totalRead;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(_length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (target < 0 || target > _length)
            {
                throw new IOException(
                    "Seek position is outside the image.");
            }

            _position = target;
            return _position;
        }

        private void FillTo(long target)
        {
            if (target > _maximumBufferedBytes)
            {
                throw new IOException(
                    $"Reading this image requires caching at least " +
                    $"{target / (1024.0 * 1024.0):N0} MB, exceeding the configured " +
                    $"{_maximumBufferedBytes / (1024.0 * 1024.0):N0} MB limit. " +
                    "Increase the archive cache limit to continue.");
            }

            while (_bufferedLength < target && !_eof)
            {
                int blockLength = (int)Math.Min(
                    BlockSize,
                    Math.Min(
                        _length - _bufferedLength,
                        _maximumBufferedBytes - _bufferedLength));

                if (blockLength <= 0)
                {
                    throw new IOException(
                        "The archive cache limit was reached before the requested data.");
                }

                byte[] block = new byte[blockLength];
                int totalRead = 0;

                while (totalRead < blockLength)
                {
                    int read = _output.Read(
                        block,
                        totalRead,
                        blockLength - totalRead);

                    if (read == 0)
                    {
                        _eof = true;
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead == 0)
                    break;

                _cache.Position = _bufferedLength;
                _cache.Write(block, 0, totalRead);
                _bufferedLength += totalRead;
            }

            if (_bufferedLength < target)
            {
                throw new EndOfStreamException(
                    "7-Zip ended before the requested image data was available.");
            }
        }

        public override void Flush()
        {
            // Nothing to flush; writes are internal to the cache.
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;

                try
                {
                    _output.Dispose();

                    if (!_process.HasExited)
                        _process.Kill(entireProcessTree: true);

                    _process.WaitForExit();
                }
                catch
                {
                    // Do not mask an exception from the caller.
                }

                try
                {
                    _cache.Dispose();
                }
                catch
                {
                    // Best-effort cleanup.
                }

                _process.Dispose();
                TryDeleteCacheFile();
            }

            base.Dispose(disposing);
        }

        private void TryDeleteCacheFile()
        {
            try
            {
                if (File.Exists(_cachePath))
                    File.Delete(_cachePath);
            }
            catch
            {
                // The operating system may clean up a leftover temporary file.
            }
        }
    }
}