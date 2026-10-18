
using XISO.Core.Models;
using XISO.OriginalXbox;

namespace XISO.Toolkit;

internal sealed class GameAnalysisService
{
    private readonly XboxGameImageAnalyzer _analyzer = new();

    private string? _lastAnalyzedSourcePath;
    private GameImageInfo? _lastAnalyzedGame;

    public bool TryGetCachedAnalysis(
        string sourcePath,
        out GameImageInfo? game)
    {
        game = null;

        if (_lastAnalyzedGame is null ||
            string.IsNullOrWhiteSpace(_lastAnalyzedSourcePath))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(sourcePath);

        if (!string.Equals(
            fullPath,
            _lastAnalyzedSourcePath,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        game = _lastAnalyzedGame;
        return true;
    }

    public bool TryGetLastAnalyzedSourcePath(
        out string? sourcePath)
    {
        sourcePath = null;

        if (_lastAnalyzedGame is null ||
            string.IsNullOrWhiteSpace(_lastAnalyzedSourcePath) ||
            !File.Exists(_lastAnalyzedSourcePath))
        {
            return false;
        }

        sourcePath = _lastAnalyzedSourcePath;
        return true;
    }

    public GameImageInfo Analyze(
        ArchiveImageSource source,
        out bool hasCachedAnalysis)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (TryGetCachedAnalysis(source.ImagePath, out var cachedGame))
        {
            hasCachedAnalysis = true;
            return cachedGame!;
        }

        hasCachedAnalysis = false;

        GameImageInfo game = _analyzer.Analyze(
            source.ImageStream,
            source.ImageName,
            source.ImageLength);

        CacheAnalysis(source.ImagePath, game);

        return game;
    }

    public void CacheAnalysis(
        string sourcePath,
        GameImageInfo game)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(game);

        _lastAnalyzedSourcePath = Path.GetFullPath(sourcePath);
        _lastAnalyzedGame = game;
    }
}
