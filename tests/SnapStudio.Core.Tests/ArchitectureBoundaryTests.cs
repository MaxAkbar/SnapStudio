using SnapStudio.Core.Capture;

namespace SnapStudio.Core.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferencePlatformOrUiAssemblies()
    {
        string[] referencedAssemblies = typeof(CaptureRequest)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("SnapStudio.App", referencedAssemblies);
        Assert.DoesNotContain("SnapStudio.CaptureHost", referencedAssemblies);
        Assert.DoesNotContain("SnapStudio.Platform.Windows", referencedAssemblies);
        Assert.DoesNotContain("SnapStudio.Ipc", referencedAssemblies);
        Assert.DoesNotContain("SnapStudio.Rendering", referencedAssemblies);
        Assert.DoesNotContain("SnapStudio.Storage", referencedAssemblies);
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Microsoft.UI", StringComparison.Ordinal));
        Assert.DoesNotContain(referencedAssemblies, name => name.StartsWith("Windows.", StringComparison.Ordinal));
    }
}
