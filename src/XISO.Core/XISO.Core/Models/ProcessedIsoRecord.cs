using XISO.Core.Detection;

namespace XISO.Core.Models;

public sealed class ProcessedIsoRecord
{
    public string IsoPath { get; set; } = string.Empty;

    public string IsoFileName { get; set; } = string.Empty;

    public long IsoFileSize { get; set; }

    public DateTime ProcessedAt { get; set; }

    public XboxPlatform Platform { get; set; }

    public string TitleId { get; set; } = string.Empty;

    public string MediaId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public OperationStatus ProcessingStatus { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public InstallationMethod InstallationMethod { get; set; }

    public string InstallationPath { get; set; } = string.Empty;

    public List<TitleUpdateRecord> TitleUpdates { get; set; } = [];
}