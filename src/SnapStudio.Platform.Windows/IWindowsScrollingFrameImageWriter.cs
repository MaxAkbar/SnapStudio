using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Platform.Windows;

public interface IWindowsScrollingFrameImageWriter
{
    ImageAsset Capture(
        RectD bounds,
        string outputPath,
        CancellationToken cancellationToken);
}
