using System.Xml.Linq;

namespace SnapStudio.Core.Tests;

public sealed class ProjectReferenceBoundaryTests
{
    [Fact]
    public void CaptureHostProject_DoesNotReferenceStorageOrApp()
    {
        XDocument project = XDocument.Load(GetRepositoryPath(
            "src",
            "SnapStudio.CaptureHost",
            "SnapStudio.CaptureHost.csproj"));

        string[] references = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(references, reference => ContainsProjectName(reference, "SnapStudio.Core"));
        Assert.Contains(references, reference => ContainsProjectName(reference, "SnapStudio.Platform.Windows"));
        Assert.DoesNotContain(references, reference => ContainsProjectName(reference, "SnapStudio.App"));
        Assert.DoesNotContain(references, reference => ContainsProjectName(reference, "SnapStudio.Storage"));
    }

    private static string GetRepositoryPath(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SnapStudio repository root.");
    }

    private static bool ContainsProjectName(string reference, string projectName)
    {
        return reference.Contains(projectName, StringComparison.OrdinalIgnoreCase);
    }
}
