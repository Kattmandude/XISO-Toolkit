using XISO.Core.Models;

namespace XISO.Core.Installation;

public interface IGameLibraryScanner
{
    IReadOnlyList<InstalledGameRecord> Scan(string libraryPath);
}