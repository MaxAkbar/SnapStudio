namespace SnapStudio.Platform.Windows;

public sealed record WindowsCaptureTargetChoice(WindowsDisplayMonitor? Monitor, WindowsTopLevelWindow? Window)
{
    public string Title => Monitor?.Title ?? Window!.Title;
    public string Description => Monitor is not null ? $"Display · {Monitor.Resolution}" : $"Window · {Window!.Description}";
}

public interface IWindowsCaptureTargetPicker
{
    Task<WindowsCaptureTargetChoice?> PickTargetAsync(
        Func<IReadOnlyList<WindowsCaptureTargetChoice>> getTargets,
        CancellationToken cancellationToken);

    Task<WindowsDisplayMonitor?> PickDisplayAsync(
        Func<IReadOnlyList<WindowsDisplayMonitor>> getMonitors,
        CancellationToken cancellationToken);

    Task<WindowsTopLevelWindow?> PickWindowAsync(
        Func<IReadOnlyList<WindowsTopLevelWindow>> getWindows,
        CancellationToken cancellationToken);
}
