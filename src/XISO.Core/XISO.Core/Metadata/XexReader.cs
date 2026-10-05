namespace XISO.Core.Metadata;

public class XexReader
{
    public bool IsValidXex(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var stream = File.OpenRead(filePath);

        Span<byte> magic = stackalloc byte[4];

        int bytesRead = stream.Read(magic);

        if (bytesRead != 4)
            return false;

        return magic.SequenceEqual("XEX2"u8);
    }
}