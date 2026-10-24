using System.Drawing;
using System.Drawing.Imaging;
using SnapStudio.Core.Capture;
using SnapStudio.Platform.Windows;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class WindowsImageImportServiceTests
{
    [TestMethod]
    public async Task FileImportCopiesImageWithoutChangingOriginalAndReportsInvalidImages()
    {
        using var workspace = TemporaryWorkspace.Create();
        string original = Path.Combine(workspace.Path, "Original.png");
        using (var bitmap = new Bitmap(20, 10)) bitmap.Save(original, ImageFormat.Png);
        byte[] originalBytes = await File.ReadAllBytesAsync(original);
        string destination = Path.Combine(workspace.Path, "SourceImages");
        var importer = new WindowsImageImportService(0, destination);
        CaptureOutcome result = await importer.ImportFileAsync(original, CancellationToken.None);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(20, result.Capture!.SourceImage.Width);
        Assert.AreEqual(10, result.Capture.SourceImage.Height);
        Assert.AreNotEqual(original, result.Capture.SourceImage.Path);
        Assert.AreEqual("Original", result.Capture.Metadata["title"]);
        Assert.AreEqual(original, result.Capture.Metadata["originalPath"]);
        Assert.AreSequenceEqual(originalBytes, await File.ReadAllBytesAsync(result.Capture.SourceImage.Path));
        Assert.AreSequenceEqual(originalBytes, await File.ReadAllBytesAsync(original));

        string broken = Path.Combine(workspace.Path, "broken.png");
        await File.WriteAllTextAsync(broken, "not an image");
        CaptureOutcome failure = await importer.ImportFileAsync(broken, CancellationToken.None);
        Assert.IsFalse(failure.Succeeded);
        Assert.HasCount(1, Directory.GetFiles(destination));
    }
}
