using System.Text;

namespace XISO.Core.Metadata;

public sealed class XdbfTitleReader
{
    private static readonly byte[] XstrMagic =
        Encoding.ASCII.GetBytes("XSTR");

    public string? ReadTitle(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        int xstrOffset = FindXstrOffset(data);

        if (xstrOffset < 0)
            return null;

        // XSTR header is 16 bytes.
        // The first string record begins immediately after it.
        int position = xstrOffset + 16;

        if (position + 2 > data.Length)
            return null;

        // The first record begins with a big-endian string length.
        int stringLength =
            (data[position] << 8) | data[position + 1];

        position += 2;

        if (stringLength <= 0 ||
            position + stringLength > data.Length)
        {
            return null;
        }

        string title =
            Encoding.UTF8.GetString(
                data,
                position,
                stringLength);

        title = title.Trim('\0', ' ', '\t', '\r', '\n');

        return string.IsNullOrWhiteSpace(title)
            ? null
            : title;
    }

    private static int FindXstrOffset(byte[] data)
    {
        for (int i = 0; i <= data.Length - XstrMagic.Length; i++)
        {
            if (data[i] == XstrMagic[0] &&
                data[i + 1] == XstrMagic[1] &&
                data[i + 2] == XstrMagic[2] &&
                data[i + 3] == XstrMagic[3])
            {
                return i;
            }
        }

        return -1;
    }
}