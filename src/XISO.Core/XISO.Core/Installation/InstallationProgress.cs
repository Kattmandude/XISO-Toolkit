
namespace XISO.Core.Installation;

public sealed record InstallationProgress(
    string Stage,
    string Message,
    int? Percent = null);