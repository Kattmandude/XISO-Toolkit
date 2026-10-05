using XISO.Core.Models;

namespace XISO.Core.Installation;

public sealed class GameInstallationRequest
{
    public string IsoPath { get; init; } = string.Empty;

    public string DestinationFolder { get; init; } = string.Empty;

    public InstallationMethod Method { get; init; }

    public string IsoFileName { get; init; } = string.Empty;
}