using XISO.Core.Detection;

namespace XISO.Core.Models;

public sealed class InstalledGameRecord
{
    public string InstallationPath { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    public XboxPlatform Platform { get; set; }

    public string TitleId { get; set; } = string.Empty;

    public string MediaId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Executable { get; set; } = string.Empty;
}