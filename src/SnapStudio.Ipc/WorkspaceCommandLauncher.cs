using System.Diagnostics;
using SnapStudio.Core.Integration;

namespace SnapStudio.Ipc;

/// <summary>Reuse the running editor before considering a cold launch. Never resend an accepted command.</summary>
public sealed class WorkspaceCommandLauncher(string pipeName)
{
    public async Task<WorkspaceCommandResponse> ExecuteAsync(string command, string? executablePath, CancellationToken token)
    {
        var request = new WorkspaceCommandRequest(1, Guid.NewGuid(), command);
        var client = new WorkspaceCommandClient(pipeName);
        try { return await client.SendAsync(request, TimeSpan.FromMilliseconds(400), token).ConfigureAwait(false); }
        catch (TimeoutException) { }

        // A busy or still-starting editor must not cause a duplicate window. Older running
        // editors also need to be closed by the user, rather than silently replaced.
        bool running = IsEditorRunning();
        if (!running)
        {
            string path = WorkspaceAppLocation.Resolve(executablePath)
                ?? throw new InvalidOperationException("Open the updated SnapStudio once, or set its application path in this extension's settings.");
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path)!
            }) ?? throw new InvalidOperationException("SnapStudio could not be started. Open it manually and try again.");
        }
        return await client.SendAsync(request, TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
    }

    private static bool IsEditorRunning()
    {
        using var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName("SnapStudio.App");
        try { return processes.Any(process => process.SessionId == current.SessionId); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
