using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using SnapStudio.Core.Integration;
using SnapStudio.Ipc;

namespace SnapStudio.CommandPalette;

public sealed partial class SnapStudioCommandsProvider : CommandProvider
{
    private readonly SettingsManager _settings = new();
    private readonly ICommandItem[] _commands;
    private int _executing;

    public SnapStudioCommandsProvider()
    {
        Id = "SnapStudio";
        DisplayName = "SnapStudio";
        Icon = IconHelpers.FromRelativePath("Assets\\Square44x44Logo.png");
        Settings = _settings.Settings;
        _commands = [
            Create(WorkspaceCommandRequest.CaptureRegion, "SnapStudio: Capture a region", "Select an area of your screen and open it for annotation."),
            Create(WorkspaceCommandRequest.EditClipboard, "SnapStudio: Edit clipboard image", "Open the clipboard image as a new editable document."),
            Create(WorkspaceCommandRequest.OpenLatest, "SnapStudio: Open most recent image", "Reopen your most recently updated image, across all folders and tags.")
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;
    public override ICommand? GetCommand(string id) => _commands.FirstOrDefault(item => item.Command.Id == id)?.Command;

    private CommandItem Create(string action, string title, string subtitle)
    {
        var command = new AnonymousCommand(() => Begin(action)) { Id = "SnapStudio." + action, Name = title, Icon = Icon };
        return new CommandItem(command)
        {
            Title = title,
            Subtitle = subtitle,
            Icon = Icon,
            MoreCommands = [new CommandContextItem(_settings.Settings.SettingsPage)]
        };
    }

    private void Begin(string action)
    {
        if (Interlocked.CompareExchange(ref _executing, 1, 0) != 0)
        {
            new ToastStatusMessage("A SnapStudio command is already running.").Show();
            return;
        }
        string path = _settings.ApplicationPath;
        _ = Task.Run(async () =>
        {
            try
            {
                // Invoke returns Dismiss immediately. Let the palette close before opening the region selector.
                await Task.Delay(300);
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var result = await new WorkspaceCommandLauncher(WorkspaceCommandServer.DefaultPipeName).ExecuteAsync(action, path, deadline.Token);
                if (!result.Succeeded) new ToastStatusMessage(result.Error ?? "SnapStudio could not complete that command.") { Duration = 8000 }.Show();
            }
            catch (Exception error)
            {
                string message = error is OperationCanceledException ? "The SnapStudio command timed out. Check SnapStudio before retrying." : error.Message;
                new ToastStatusMessage(message) { Duration = 8000 }.Show();
            }
            finally { Volatile.Write(ref _executing, 0); }
        });
    }
}
