using XISO.Core.Models;

namespace XISO.Core.Analysis;

public interface IImageAnalyzer
{
    GameImageInfo Analyze(string folderPath);
}