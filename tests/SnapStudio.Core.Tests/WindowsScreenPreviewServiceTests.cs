using SnapStudio.Core.Capture;
using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsScreenPreviewServiceTests
{
    [Fact]
    public async Task CapturePreviewAsync_WhenBoundsAreEmpty_ReturnsNull()
    {
        var service = new WindowsScreenPreviewService();

        ScreenPreviewImage? preview = await service.CapturePreviewAsync(
            new RectD(0, 0, 0, 100),
            CancellationToken.None);

        Assert.Null(preview);
    }
}
