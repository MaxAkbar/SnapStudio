using SnapStudio.Core.Ocr;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsOcrProviderFactory
{
    private readonly IWindowsPackageIdentityService _packageIdentityService;

    public WindowsOcrProviderFactory(
        IWindowsPackageIdentityService? packageIdentityService = null)
    {
        _packageIdentityService = packageIdentityService ?? new WindowsPackageIdentityService();
    }

    public IOcrProvider Create()
    {
        return _packageIdentityService.HasPackageIdentity()
            ? new WindowsOcrProvider()
            : new UnavailableOcrProvider();
    }
}
