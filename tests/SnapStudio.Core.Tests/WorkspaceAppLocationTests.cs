using SnapStudio.Ipc;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WorkspaceAppLocationTests
{
    [TestMethod]
    public void DiscoveryRequiresACompatibleEditorAndSurvivesMovedOrCorruptFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SnapStudio.location.test." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string executable = Path.Combine(directory, "SnapStudio.App.exe");
            string location = Path.Combine(directory, "workspace-app.json");
            string manifest = Path.Combine(directory, "SnapStudio.integration.json");
            File.WriteAllText(executable, "fixture");
            Assert.IsFalse(WorkspaceAppLocation.TryRemember(executable, location));
            File.WriteAllText(manifest, "{\"externalCaptureProtocol\":1}");
            Assert.IsFalse(WorkspaceAppLocation.TryRemember(executable, location));
            File.WriteAllText(manifest, "{\"workspaceCommandProtocol\":1}");
            Assert.IsTrue(WorkspaceAppLocation.TryRemember(executable, location));
            Assert.AreEqual(executable, WorkspaceAppLocation.Resolve(locationFile: location));
            Assert.IsNull(WorkspaceAppLocation.Resolve("missing.exe", location));
            File.WriteAllText(location, "corrupt");
            Assert.IsNull(WorkspaceAppLocation.Resolve(locationFile: location));
            Assert.AreEqual(executable, WorkspaceAppLocation.Resolve('"' + executable + '"', location));
            File.Delete(executable);
            Assert.IsNull(WorkspaceAppLocation.Resolve(executable, location));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void UnwritableDiscoveryIsBestEffort()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SnapStudio.location.test." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string executable = Path.Combine(directory, "SnapStudio.App.exe");
            File.WriteAllText(executable, "fixture");
            File.WriteAllText(Path.Combine(directory, "SnapStudio.integration.json"), "{\"workspaceCommandProtocol\":1}");
            // A directory cannot be replaced by the discovery JSON file.
            Assert.IsFalse(WorkspaceAppLocation.TryRemember(executable, directory));
            Assert.IsTrue(File.Exists(executable));
            Assert.IsFalse(Directory.EnumerateFiles(Path.GetDirectoryName(directory)!, Path.GetFileName(directory) + ".*.tmp").Any());
            Assert.IsNull(WorkspaceAppLocation.Resolve(locationFile: "\0"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
