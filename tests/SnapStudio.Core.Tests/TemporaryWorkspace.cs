namespace SnapStudio.Core.Tests;

internal sealed class TemporaryWorkspace : IDisposable
{
    private TemporaryWorkspace(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    public string Path { get; }

    public static TemporaryWorkspace Create()
    {
        string path = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            "SnapStudio.Tests",
            Guid.NewGuid().ToString("N"));

        return new TemporaryWorkspace(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
