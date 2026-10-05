using XISO.OriginalXbox;

namespace XISO.OriginalXbox.Tests;

public class XboxIsoReaderTests
{
    [Fact]
    public void ReadsWorldOfOutlawsVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\World of Outlaws Sprint Cars (EU).iso";

        using var reader = new XboxIsoReader(isoPath);

        Assert.Equal(0x0FDA0000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x000AD298U, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x00000800U, reader.VolumeDescriptor.RootDirectorySize);
    }

    [Fact]
    public void ReadsNinjaGaidenIIVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\Ninja Gaiden II (World) (En,Ja,Fr,De,Es,It,Zh,Ko,Pl,Ru).iso";

        using var reader = new XboxIsoReader(isoPath);

        Assert.Equal(0x0FDA0000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x000094ECU, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x00000800U, reader.VolumeDescriptor.RootDirectorySize);
    }

    [Fact]
    public void ReadsPandoraTomorrowVolumeDescriptor()
    {
        const string isoPath =
            @"F:\Downloads\Tom Clancy's Splinter Cell - Pandora Tomorrow (USA, Europe) (En,Fr,De,Es,It).xiso.iso";

        using var reader = new XboxIsoReader(isoPath);

        Assert.Equal(0x00010000L, reader.VolumeDescriptor.Offset);
        Assert.Equal(0x00000108U, reader.VolumeDescriptor.RootDirectorySector);
        Assert.Equal(0x0000016CU, reader.VolumeDescriptor.RootDirectorySize);
    }
}
