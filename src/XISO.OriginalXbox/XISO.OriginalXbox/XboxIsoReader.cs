namespace XISO.OriginalXbox;

public sealed class XboxIsoReader : IDisposable
{
    private readonly FileStream _stream;

    public string IsoPath { get; }

    public XboxIsoReader(string isoPath)
    {
        if (string.IsNullOrWhiteSpace(isoPath))
            throw new ArgumentException("ISO path cannot be empty.", nameof(isoPath));

        if (!File.Exists(isoPath))
            throw new FileNotFoundException("ISO file was not found.", isoPath);

        IsoPath = Path.GetFullPath(isoPath);

        _stream = new FileStream(
            IsoPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
