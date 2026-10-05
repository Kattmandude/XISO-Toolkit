namespace XISO.Core.Detection;

public class IsoPlatformDetector
{
    public XboxPlatform Detect(string extractedFolder)
    {
        if (File.Exists(Path.Combine(extractedFolder, "default.xex")))
        {
            return XboxPlatform.Xbox360;
        }

        if (File.Exists(Path.Combine(extractedFolder, "default.xbe")))
        {
            return XboxPlatform.OriginalXbox;
        }

        return XboxPlatform.Unknown;
    }
}