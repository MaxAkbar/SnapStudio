namespace SnapStudio.Core.System;

public sealed record HotkeyRegistration(string Name, string Gesture);

public sealed record HotkeyPressedEvent(string Name, string Gesture);

public sealed record HotkeyRegistrationResult(bool Succeeded, string? ErrorMessage)
{
    public static HotkeyRegistrationResult Success() => new(true, null);

    public static HotkeyRegistrationResult Failed(string errorMessage) => new(false, errorMessage);
}

public interface IHotkeyService
{
    event EventHandler<HotkeyPressedEvent>? HotkeyPressed;

    Task<HotkeyRegistrationResult> RegisterAsync(
        HotkeyRegistration registration,
        CancellationToken cancellationToken);

    Task UnregisterAsync(string name, CancellationToken cancellationToken);
}
