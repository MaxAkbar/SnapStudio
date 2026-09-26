using SnapStudio.Core.System;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsHotkeyServiceTests
{
    [TestMethod]
    public async Task RegisterAsync_WhenGestureIsUnsupported_ReturnsFailureBeforeNativeRegistration()
    {
        var hotkeys = new WindowsHotkeyService(ownerWindowHandle: 0);

        HotkeyRegistrationResult result = await hotkeys.RegisterAsync(
            new HotkeyRegistration("Capture", "Ctrl+UnsupportedKey"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.Contains("UnsupportedKey", result.ErrorMessage ?? string.Empty);
    }

    [TestMethod]
    public async Task RegisterAsync_WhenOwnerWindowIsMissing_ReturnsFailure()
    {
        var hotkeys = new WindowsHotkeyService(ownerWindowHandle: 0);

        HotkeyRegistrationResult result = await hotkeys.RegisterAsync(
            new HotkeyRegistration("Capture", "Ctrl+Shift+S"),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.Contains("owner window", result.ErrorMessage ?? string.Empty);
    }
}
