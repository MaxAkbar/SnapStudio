using SnapStudio.Core.Capture;
using SnapStudio.Core.Ocr;
using SnapStudio.Core.Primitives;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

public sealed class WindowsOcrProviderTests
{
    [Fact]
    public void WindowsOcrProviderFactory_WhenProcessIsPackaged_ReturnsWindowsProvider()
    {
        var factory = new WindowsOcrProviderFactory(new FakePackageIdentityService(hasPackageIdentity: true));

        IOcrProvider provider = factory.Create();

        Assert.IsType<WindowsOcrProvider>(provider);
    }

    [Fact]
    public void WindowsOcrProviderFactory_WhenProcessIsUnpackaged_ReturnsUnavailableProvider()
    {
        var factory = new WindowsOcrProviderFactory(new FakePackageIdentityService(hasPackageIdentity: false));

        IOcrProvider provider = factory.Create();

        Assert.IsType<UnavailableOcrProvider>(provider);
    }

    [Fact]
    public async Task WindowsOcrProvider_GetStatusAsync_ReturnsProviderStatus()
    {
        var provider = new WindowsOcrProvider();

        OcrProviderStatus status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Equal("Windows OCR", status.ProviderName);
    }

    [Fact]
    public async Task WindowsOcrProvider_RecognizeAsync_WhenImageIsMissing_ReturnsImageUnavailable()
    {
        var provider = new WindowsOcrProvider();

        OcrRecognitionResult result = await provider.RecognizeAsync(
            new OcrRequest(
                DocumentId.New(),
                new ImageAsset(
                    Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"),
                    100,
                    40,
                    ImagePixelFormat.Bgra32),
                null),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(OcrFailureReason.ImageUnavailable, result.Failure?.Reason);
    }

    private sealed class FakePackageIdentityService(bool hasPackageIdentity)
        : IWindowsPackageIdentityService
    {
        public bool HasPackageIdentity() => hasPackageIdentity;
    }
}
