
using XISO.Core.Metadata;

namespace XISO.OriginalXbox.Tests;

public class Xex2RegionDecoderTests
{
    [Theory]
    [InlineData(0x000000FFu, "NTSC-U")]
    [InlineData(0x00FF0000u, "PAL")]
    [InlineData(0xFFFFFFFFu, "All Regions")]
    [InlineData(0x0000FDFFu, "NTSC-U, NTSC-J")]
    [InlineData(0x00000100u, "Japan")]
    [InlineData(0x00000200u, "China")]
    [InlineData(0x00010000u, "Australia/New Zealand")]
    public void DecodesKnownRegionMasks(
        uint regionMask,
        string expected)
    {
        string actual = Xex2RegionDecoder.Decode(regionMask);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(0x00000000u, "Unknown (0x00000000)")]
    [InlineData(0x00000001u, "NTSC-U")]
    [InlineData(0x00000101u, "NTSC-U, NTSC-J")]
    [InlineData(0x00020000u, "PAL")]
    public void DecodesAdditionalRegionMasks(
        uint regionMask,
        string expected)
    {
        string actual = Xex2RegionDecoder.Decode(regionMask);

        Assert.Equal(expected, actual);
    }
}
