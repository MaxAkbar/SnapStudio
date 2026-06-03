using SnapStudio.Core.System;

namespace SnapStudio.Platform.Windows;

public sealed class UnsupportedHotkeyService : IHotkeyService
{
    public event EventHandler<HotkeyPressedEvent>? HotkeyPressed
    {
        add { }
        remove { }
    }

    public Task<HotkeyRegistrationResult> RegisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(HotkeyRegistrationResult.Failed(
            "Global hotkey registration is deferred until the capture MVP phase."));
    }

    public Task UnregisterAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}
