using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SnapStudio.Core.Export;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Platform.Windows;

public sealed class PdfDocumentExportProvider : IExportProvider
{
    private const double PointsPerPixelAt96Dpi = 72.0 / 96.0;
    private readonly IDocumentRenderer _documentRenderer;

    public PdfDocumentExportProvider(IDocumentRenderer documentRenderer)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _documentRenderer = documentRenderer;
    }

    public ExportFormat Format => ExportFormat.Pdf;

    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Format != Format)
        {
            return ExportResult.Failed(
                $"The {Format} export provider cannot export {request.Format}.");
        }

        RenderResult renderResult = await _documentRenderer
            .RenderAsync(new RenderRequest(request.DocumentId, 1, null), cancellationToken)
            .ConfigureAwait(false);

        if (!renderResult.Succeeded || renderResult.Image is not RenderedImage image)
        {
            return ExportResult.Failed(renderResult.ErrorMessage ?? "The document could not be rendered.");
        }

        string? outputDirectory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        try
        {
            byte[] pdfBytes = await Task
                .Run(() => BuildPdfDocument(image.Pixels, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            await File
                .WriteAllBytesAsync(request.OutputPath, pdfBytes, cancellationToken)
                .ConfigureAwait(false);

            return ExportResult.Success(request.OutputPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or ExternalException
            or IOException
            or UnauthorizedAccessException)
        {
            return ExportResult.Failed($"PDF export failed: {exception.Message}");
        }
    }

    private static byte[] BuildPdfDocument(
        byte[] pngBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var input = new MemoryStream(pngBytes);
        using var source = Image.FromStream(input);
        using var flattened = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(flattened))
        {
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        using var jpegStream = new MemoryStream();
        flattened.Save(jpegStream, ImageFormat.Jpeg);
        byte[] jpegBytes = jpegStream.ToArray();

        double pageWidth = Math.Max(1, source.Width * PointsPerPixelAt96Dpi);
        double pageHeight = Math.Max(1, source.Height * PointsPerPixelAt96Dpi);
        byte[] contentBytes = Encoding.ASCII.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"q\n{FormatPdfNumber(pageWidth)} 0 0 {FormatPdfNumber(pageHeight)} 0 0 cm\n/Im0 Do\nQ\n"));

        using var output = new MemoryStream();
        long[] offsets = new long[7];

        output.Write([37, 80, 68, 70, 45, 49, 46, 52, 10, 37, 255, 255, 255, 255, 10]);
        WriteObject(output, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>\n");
        WriteObject(output, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n");
        WriteObject(
            output,
            offsets,
            3,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatPdfNumber(pageWidth)} {FormatPdfNumber(pageHeight)}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\n"));
        WriteImageObject(output, offsets, 4, source.Width, source.Height, jpegBytes);
        WriteStreamObject(output, offsets, 5, contentBytes);
        WriteObject(
            output,
            offsets,
            6,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Producer (SnapStudio) /CreationDate ({CreatePdfDate(DateTimeOffset.UtcNow)}) >>\n"));
        WriteCrossReference(output, offsets);

        return output.ToArray();
    }

    private static void WriteObject(
        Stream output,
        long[] offsets,
        int objectNumber,
        string body)
    {
        offsets[objectNumber] = output.Position;
        WriteAscii(output, $"{objectNumber} 0 obj\n");
        WriteAscii(output, body);
        WriteAscii(output, "endobj\n");
    }

    private static void WriteImageObject(
        Stream output,
        long[] offsets,
        int objectNumber,
        int width,
        int height,
        byte[] jpegBytes)
    {
        offsets[objectNumber] = output.Position;
        WriteAscii(output, $"{objectNumber} 0 obj\n");
        WriteAscii(
            output,
            string.Create(
                CultureInfo.InvariantCulture,
                $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n"));
        output.Write(jpegBytes);
        WriteAscii(output, "\nendstream\nendobj\n");
    }

    private static void WriteStreamObject(
        Stream output,
        long[] offsets,
        int objectNumber,
        byte[] streamBytes)
    {
        offsets[objectNumber] = output.Position;
        WriteAscii(output, $"{objectNumber} 0 obj\n");
        WriteAscii(output, $"<< /Length {streamBytes.Length} >>\nstream\n");
        output.Write(streamBytes);
        WriteAscii(output, "endstream\nendobj\n");
    }

    private static void WriteCrossReference(Stream output, long[] offsets)
    {
        long crossReferenceOffset = output.Position;
        WriteAscii(output, $"xref\n0 {offsets.Length}\n");
        WriteAscii(output, "0000000000 65535 f \n");

        for (int index = 1; index < offsets.Length; index++)
        {
            WriteAscii(output, $"{offsets[index]:D10} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {offsets.Length} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n");
    }

    private static void WriteAscii(Stream output, string value)
    {
        output.Write(Encoding.ASCII.GetBytes(value));
    }

    private static string FormatPdfNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string CreatePdfDate(DateTimeOffset dateTime)
    {
        return dateTime.UtcDateTime.ToString("'D:'yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture);
    }
}
