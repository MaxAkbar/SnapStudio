using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using SnapStudio.Core.Documents;
using SnapStudio.Core.Primitives;
using SnapStudio.Core.Rendering;

namespace SnapStudio.Rendering;

public sealed class SystemDrawingDocumentRenderer : IDocumentRenderer
{
    private const string EncodedPngFormat = "Png";
    private readonly IDocumentRepository _documentRepository;

    public SystemDrawingDocumentRenderer(IDocumentRepository documentRepository)
    {
        ArgumentNullException.ThrowIfNull(documentRepository);

        _documentRepository = documentRepository;
    }

    public async Task<RenderResult> RenderAsync(
        RenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CaptureDocument? document;
        try
        {
            document = await _documentRepository
                .GetAsync(request.DocumentId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return RenderResult.Failed(exception.Message);
        }

        if (document is null)
        {
            return RenderResult.Failed("The selected document could not be found.");
        }

        try
        {
            DocumentLayers.ValidateForSave(document);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return RenderResult.Failed(exception.Message);
        }

        if (!File.Exists(document.SourceImage.Path))
        {
            return RenderResult.Failed("The source image for the selected document could not be found.");
        }

        return await Task.Run(
                () => RenderDocument(document, request, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static RenderResult RenderDocument(
        CaptureDocument document,
        RenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using Bitmap source = CreateBitmap(document.SourceImage.Path);
        Bitmap? editedBitmap = null;
        try
        {
            Bitmap baseBitmap = source;
            if (document.DestructiveOperations.Count > 0)
            {
                editedBitmap = ApplyDestructiveOperations(source, document, cancellationToken);
                baseBitmap = editedBitmap;
            }

            using Bitmap outputBitmap = ApplyViewportAndScale(
                baseBitmap,
                request,
                out RectD viewport,
                out double scale);

            using (Graphics graphics = Graphics.FromImage(outputBitmap))
            {
                ConfigureGraphics(graphics);

                foreach (AnnotationObject annotation in DocumentLayers.InPaintOrder(document))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!DocumentLayers.IsEffectivelyVisible(document, annotation))
                    {
                        continue;
                    }

                    DrawAnnotation(
                        graphics,
                        outputBitmap,
                        TransformAnnotationForRender(annotation, viewport, scale),
                        scale);
                }
            }

            if (request.Selection is PixelSelection selection)
            {
                if (selection.IsEmpty || selection.Width != document.SourceImage.Width || selection.Height != document.SourceImage.Height)
                    return RenderResult.Failed("The selection does not match the current image.");
                using Graphics graphics = Graphics.FromImage(outputBitmap);
                using var selected = new Region();
                selected.MakeEmpty();
                foreach (RectD rectangle in selection.GetRectangles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    selected.Union(new RectangleF((float)((rectangle.X - viewport.X) * scale), (float)((rectangle.Y - viewport.Y) * scale),
                        (float)(rectangle.Width * scale), (float)(rectangle.Height * scale)));
                }
                graphics.SetClip(selected, CombineMode.Exclude);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using var transparent = new SolidBrush(Color.Transparent);
                graphics.FillRectangle(transparent, 0, 0, outputBitmap.Width, outputBitmap.Height);
            }

            using var stream = new MemoryStream();
            outputBitmap.Save(stream, ImageFormat.Png);

            return RenderResult.Success(new RenderedImage(
                outputBitmap.Width,
                outputBitmap.Height,
                stream.ToArray(),
                EncodedPngFormat));
        }
        finally
        {
            editedBitmap?.Dispose();
        }
    }

    private static Bitmap CreateBitmap(string sourcePath)
    {
        using var loaded = new Bitmap(sourcePath);
        return new Bitmap(loaded);
    }

    private static Bitmap ApplyDestructiveOperations(
        Bitmap source,
        CaptureDocument document,
        CancellationToken cancellationToken)
    {
        Bitmap current = new(source);

        foreach (DestructiveEditOperation operation in document.DestructiveOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Bitmap next = operation.Kind switch
            {
                DestructiveOperationKind.Crop when operation.Bounds is RectD bounds =>
                    Crop(current, bounds),
                DestructiveOperationKind.Resize => Resize(current, operation.Parameters),
                DestructiveOperationKind.RasterBlur when operation.Bounds is RectD bounds =>
                    BlurRegion(current, bounds, ResolveBlurRadius(operation.Parameters, fallback: 10)),
                DestructiveOperationKind.Pixelate when operation.Bounds is RectD bounds =>
                    PixelateRegion(current, bounds, ResolvePixelSize(operation.Parameters, fallback: 8)),
                _ => new Bitmap(current)
            };

            current.Dispose();
            current = next;
        }

        return current;
    }

    private static Bitmap ApplyViewportAndScale(
        Bitmap source,
        RenderRequest request,
        out RectD resolvedViewport,
        out double resolvedScale)
    {
        RectD viewport = request.Viewport is RectD requestedViewport
            ? Normalize(requestedViewport)
            : new RectD(0, 0, source.Width, source.Height);

        int left = Math.Clamp((int)Math.Round(viewport.X), 0, source.Width);
        int top = Math.Clamp((int)Math.Round(viewport.Y), 0, source.Height);
        int right = Math.Clamp((int)Math.Round(viewport.Right), left, source.Width);
        int bottom = Math.Clamp((int)Math.Round(viewport.Bottom), top, source.Height);
        int viewportWidth = Math.Max(1, right - left);
        int viewportHeight = Math.Max(1, bottom - top);
        double scale = double.IsFinite(request.Scale) && request.Scale > 0
            ? request.Scale
            : 1;
        resolvedViewport = new RectD(left, top, viewportWidth, viewportHeight);
        resolvedScale = scale;
        int outputWidth = Math.Max(1, (int)Math.Round(viewportWidth * scale));
        int outputHeight = Math.Max(1, (int)Math.Round(viewportHeight * scale));

        var output = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(output);
        ConfigureGraphics(graphics);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, outputWidth, outputHeight),
            new Rectangle(left, top, viewportWidth, viewportHeight),
            GraphicsUnit.Pixel);

        return output;
    }

    private static AnnotationObject TransformAnnotationForRender(
        AnnotationObject annotation,
        RectD viewport,
        double scale)
    {
        return new AnnotationObject
        {
            Id = annotation.Id,
            Kind = annotation.Kind,
            LayerId = annotation.LayerId,
            IsVisible = annotation.IsVisible,
            Bounds = new RectD(
                (annotation.Bounds.X - viewport.X) * scale,
                (annotation.Bounds.Y - viewport.Y) * scale,
                annotation.Bounds.Width * scale,
                annotation.Bounds.Height * scale),
            Text = annotation.Text,
            StrokePoints = [.. annotation.StrokePoints],
            FillRectangles = [.. annotation.FillRectangles],
            Image = annotation.Image?.DeepCopy(),
            Vector = annotation.Vector,
            Style = annotation.Style with
            {
                StrokeThickness = Math.Max(annotation.Kind == AnnotationKind.Text ? 0.1 : 1, annotation.Style.StrokeThickness * scale),
                CornerRadius = Math.Max(0, annotation.Style.CornerRadius * scale)
            }
        };
    }

    private static void DrawAnnotation(
        Graphics graphics,
        Bitmap bitmap,
        AnnotationObject annotation,
        double scale)
    {
        switch (annotation.Kind)
        {
            case AnnotationKind.Vector:
                if (annotation.Vector is not null) SvgRasterizer.Draw(graphics, annotation.Vector.Svg, ToRectangleF(annotation.Bounds), annotation.Style.Opacity);
                break;
            case AnnotationKind.Image:
                DrawImageObject(graphics, annotation);
                break;
            case AnnotationKind.Fill:
                DrawFill(graphics, annotation);
                break;
            case AnnotationKind.Pen:
                DrawPenStroke(graphics, annotation);
                break;
            case AnnotationKind.Rectangle:
                DrawRectangle(graphics, annotation);
                break;
            case AnnotationKind.Ellipse:
                DrawEllipse(graphics, annotation);
                break;
            case AnnotationKind.Line:
                DrawLine(graphics, annotation);
                break;
            case AnnotationKind.Arrow:
                DrawArrow(graphics, annotation, scale);
                break;
            case AnnotationKind.Text:
                DrawText(graphics, annotation, scale);
                break;
            case AnnotationKind.Highlight:
                DrawHighlight(graphics, annotation);
                break;
            case AnnotationKind.Blur:
                BlurRegionInPlace(bitmap, annotation.Bounds, ResolveBlurRadius(annotation.Style.Opacity));
                break;
        }
    }

    private static void DrawFill(Graphics graphics, AnnotationObject annotation)
    {
        using var path = new GraphicsPath(FillMode.Winding);
        foreach (RectD rectangle in AreaFill.GetRectangles(annotation)) path.AddRectangle(ToRectangleF(rectangle));
        using Brush brush = CreateBrush(annotation.Style.Fill, annotation.Style.Opacity);
        graphics.FillPath(brush, path);
    }

    private static void DrawImageObject(Graphics graphics, AnnotationObject annotation)
    {
        if (annotation.Image is null) return;
        using var stream = new MemoryStream(annotation.Image.PngBytes, writable: false);
        using var image = Image.FromStream(stream);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)Math.Clamp(annotation.Style.Opacity, 0, 1) });
        RectD bounds = annotation.Bounds;
        PointF[] destination = [new((float)bounds.X, (float)bounds.Y), new((float)bounds.Right, (float)bounds.Y), new((float)bounds.X, (float)bounds.Bottom)];
        graphics.DrawImage(image, destination, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, attributes);
    }

    private static void DrawPenStroke(Graphics graphics, AnnotationObject annotation)
    {
        PointF[] points = FreehandStroke.GetPoints(annotation).Select(point => new PointF((float)point.X, (float)point.Y)).ToArray();
        using Pen pen = CreatePen(annotation.Style.Stroke, annotation.Style.StrokeThickness, annotation.Style.Opacity, annotation.Style.Pattern);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;
        if (points.Length == 1 || points.All(point => point == points[0]))
        {
            using Brush brush = CreateBrush(annotation.Style.Stroke, annotation.Style.Opacity);
            float radius = pen.Width / 2;
            graphics.FillEllipse(brush, points[0].X - radius, points[0].Y - radius, pen.Width, pen.Width);
        }
        else
        {
            graphics.DrawLines(pen, points);
        }
    }

    private static void DrawRectangle(Graphics graphics, AnnotationObject annotation)
    {
        RectangleF rectangle = ToRectangleF(annotation.Bounds);
        using Brush fill = CreateBrush(annotation.Style.Fill, annotation.Style.Opacity);
        using Pen pen = CreatePen(annotation.Style.Stroke, annotation.Style.StrokeThickness, annotation.Style.Opacity, annotation.Style.Pattern);
        using GraphicsPath? roundedPath = CreateRoundedRectanglePath(rectangle, annotation.Style.CornerRadius);

        if (annotation.Style.Fill.A > 0)
        {
            if (roundedPath is null)
            {
                graphics.FillRectangle(fill, rectangle);
            }
            else
            {
                graphics.FillPath(fill, roundedPath);
            }
        }

        if (annotation.Style.Stroke.A > 0 && annotation.Style.StrokeThickness > 0)
        {
            if (roundedPath is null)
            {
                graphics.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            }
            else
            {
                graphics.DrawPath(pen, roundedPath);
            }
        }
    }

    private static GraphicsPath? CreateRoundedRectanglePath(RectangleF rectangle, double cornerRadius)
    {
        float radius = (float)Math.Min(
            Math.Max(0, cornerRadius),
            Math.Min(rectangle.Width, rectangle.Height) / 2);
        if (radius <= 0.01f)
        {
            return null;
        }

        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawEllipse(Graphics graphics, AnnotationObject annotation)
    {
        RectangleF rectangle = ToRectangleF(annotation.Bounds);
        using Brush fill = CreateBrush(annotation.Style.Fill, annotation.Style.Opacity);
        using Pen pen = CreatePen(annotation.Style.Stroke, annotation.Style.StrokeThickness, annotation.Style.Opacity, annotation.Style.Pattern);

        if (annotation.Style.Fill.A > 0)
        {
            graphics.FillEllipse(fill, rectangle);
        }

        if (annotation.Style.Stroke.A > 0 && annotation.Style.StrokeThickness > 0)
        {
            graphics.DrawEllipse(pen, rectangle);
        }
    }

    private static void DrawLine(
        Graphics graphics,
        AnnotationObject annotation)
    {
        PointF start = new((float)annotation.Bounds.X, (float)annotation.Bounds.Y);
        PointF end = new((float)annotation.Bounds.Right, (float)annotation.Bounds.Bottom);
        using Pen pen = CreatePen(annotation.Style.Stroke, annotation.Style.StrokeThickness, annotation.Style.Opacity, annotation.Style.Pattern);

        graphics.DrawLine(pen, start, end);
    }

    private static void DrawArrow(
        Graphics graphics,
        AnnotationObject annotation,
        double scale)
    {
        PointF[] points = ArrowGeometry.CreateOutline(
                new PointD(annotation.Bounds.X, annotation.Bounds.Y),
                new PointD(annotation.Bounds.Right, annotation.Bounds.Bottom),
                annotation.Style.StrokeThickness, scale)
            .Select(point => new PointF((float)point.X, (float)point.Y)).ToArray();
        if (points.Length == 0)
        {
            return;
        }

        using Brush brush = CreateBrush(annotation.Style.Stroke, annotation.Style.Opacity);
        if (annotation.Style.Pattern != StrokePattern.Solid)
        {
            PointF neck = new((points[2].X + points[4].X) / 2, (points[2].Y + points[4].Y) / 2);
            using Pen pen = CreatePen(annotation.Style.Stroke, annotation.Style.StrokeThickness, annotation.Style.Opacity, annotation.Style.Pattern);
            pen.StartCap = LineCap.Flat; pen.EndCap = LineCap.Flat;
            graphics.DrawLine(pen, new PointF((float)annotation.Bounds.X, (float)annotation.Bounds.Y), neck);
            graphics.FillPolygon(brush, [points[2], points[3], points[4]]);
            return;
        }
        graphics.FillPolygon(brush, points);
    }

    private static void DrawText(Graphics graphics, AnnotationObject annotation, double scale)
    {
        RectangleF rectangle = ToRectangleF(annotation.Bounds);
        using Brush brush = CreateBrush(annotation.Style.Text, annotation.Style.Opacity);
        using Font font = SystemFontCatalog.CreateFont(annotation.Style.FontFamily, annotation.Style.StrokeThickness);
        using StringFormat format = TextAnnotationLayout.CreateFormat();

        graphics.DrawString(
            TextAnnotationLayout.DisplayText(annotation.Text),
            font,
            brush,
            new PointF(rectangle.X + (float)(TextAnnotationLayout.Padding * scale), rectangle.Y + (float)(TextAnnotationLayout.Padding * scale)),
            format);
    }

    private static void DrawHighlight(Graphics graphics, AnnotationObject annotation)
    {
        using Brush brush = CreateBrush(annotation.Style.Fill, annotation.Style.Opacity);
        graphics.FillRectangle(brush, ToRectangleF(annotation.Bounds));
    }

    private static Bitmap Crop(Bitmap source, RectD bounds)
    {
        Rectangle rectangle = ClampRectangle(bounds, source.Width, source.Height);
        var output = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(output);
        ConfigureGraphics(graphics);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, rectangle.Width, rectangle.Height),
            rectangle,
            GraphicsUnit.Pixel);

        return output;
    }

    private static Bitmap Resize(
        Bitmap source,
        IReadOnlyDictionary<string, string> parameters)
    {
        int width = TryReadPositiveInt(parameters, "width") ?? source.Width;
        int height = TryReadPositiveInt(parameters, "height") ?? source.Height;
        var output = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(output);
        ConfigureGraphics(graphics);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));

        return output;
    }

    private static Bitmap BlurRegion(Bitmap source, RectD bounds, int radius)
    {
        var output = new Bitmap(source);
        BlurRegionInPlace(output, bounds, radius);
        return output;
    }

    private static void BlurRegionInPlace(Bitmap bitmap, RectD bounds, int radius)
    {
        Rectangle rectangle = ClampRectangle(bounds, bitmap.Width, bitmap.Height);
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return;
        }

        using Bitmap region = bitmap.Clone(rectangle, PixelFormat.Format32bppPArgb);
        using Bitmap blurred = BoxBlur(region, Math.Clamp(radius, 1, 48));
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.DrawImageUnscaled(blurred, rectangle.X, rectangle.Y);
    }

    private static Bitmap PixelateRegion(Bitmap source, RectD bounds, int pixelSize)
    {
        var output = new Bitmap(source);
        Rectangle rectangle = ClampRectangle(bounds, output.Width, output.Height);
        int size = Math.Clamp(pixelSize, 2, 64);

        using Graphics graphics = Graphics.FromImage(output);
        for (int y = rectangle.Top; y < rectangle.Bottom; y += size)
        {
            for (int x = rectangle.Left; x < rectangle.Right; x += size)
            {
                int width = Math.Min(size, rectangle.Right - x);
                int height = Math.Min(size, rectangle.Bottom - y);
                Color color = output.GetPixel(x + width / 2, y + height / 2);
                using var brush = new SolidBrush(color);
                graphics.FillRectangle(brush, x, y, width, height);
            }
        }

        return output;
    }

    private static Bitmap BoxBlur(Bitmap source, int radius)
    {
        int blurScale = Math.Clamp(radius / 2, 2, 16);
        int downsampledWidth = Math.Max(1, source.Width / blurScale);
        int downsampledHeight = Math.Max(1, source.Height / blurScale);
        using var downsampled = new Bitmap(
            downsampledWidth,
            downsampledHeight,
            PixelFormat.Format32bppPArgb);

        using (Graphics graphics = Graphics.FromImage(downsampled))
        {
            ConfigureFastResampleGraphics(graphics);
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, downsampledWidth, downsampledHeight),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
        }

        var output = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using Graphics outputGraphics = Graphics.FromImage(output);
        ConfigureFastResampleGraphics(outputGraphics);
        outputGraphics.DrawImage(
            downsampled,
            new Rectangle(0, 0, source.Width, source.Height),
            new Rectangle(0, 0, downsampledWidth, downsampledHeight),
            GraphicsUnit.Pixel);
        return output;
    }

    private static Rectangle ClampRectangle(
        RectD bounds,
        int maximumWidth,
        int maximumHeight)
    {
        RectD normalized = Normalize(bounds);
        int left = Math.Clamp((int)Math.Floor(normalized.X), 0, maximumWidth);
        int top = Math.Clamp((int)Math.Floor(normalized.Y), 0, maximumHeight);
        int right = Math.Clamp((int)Math.Ceiling(normalized.Right), left, maximumWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(normalized.Bottom), top, maximumHeight);

        return new Rectangle(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    private static RectangleF ToRectangleF(RectD bounds)
    {
        RectD normalized = Normalize(bounds);
        return new RectangleF(
            (float)normalized.X,
            (float)normalized.Y,
            (float)normalized.Width,
            (float)normalized.Height);
    }

    private static RectD Normalize(RectD bounds)
    {
        double left = Math.Min(bounds.X, bounds.Right);
        double top = Math.Min(bounds.Y, bounds.Bottom);
        double right = Math.Max(bounds.X, bounds.Right);
        double bottom = Math.Max(bounds.Y, bounds.Bottom);

        return new RectD(left, top, right - left, bottom - top);
    }

    private static Pen CreatePen(
        ColorRgba color,
        double thickness,
        double opacity,
        StrokePattern pattern = StrokePattern.Solid)
    {
        var pen = new Pen(ToDrawingColor(color, opacity), (float)Math.Max(1, thickness))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (pattern != StrokePattern.Solid) pen.DashPattern = ToolDesigns.DashArray(pattern).Select(value => (float)value).ToArray();
        return pen;
    }

    private static Brush CreateBrush(ColorRgba color, double opacity)
    {
        return new SolidBrush(ToDrawingColor(color, opacity));
    }

    private static Color ToDrawingColor(ColorRgba color, double opacity)
    {
        int alpha = Math.Clamp((int)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), 0, 255);
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    private static void ConfigureFastResampleGraphics(Graphics graphics)
    {
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.HighSpeed;
    }

    private static int ResolveBlurRadius(double opacity)
    {
        return (int)Math.Clamp(Math.Round(opacity * 32), 4, 32);
    }

    private static int ResolveBlurRadius(
        IReadOnlyDictionary<string, string> parameters,
        int fallback)
    {
        return TryReadPositiveInt(parameters, "radius") ?? fallback;
    }

    private static int ResolvePixelSize(
        IReadOnlyDictionary<string, string> parameters,
        int fallback)
    {
        return TryReadPositiveInt(parameters, "size") ?? fallback;
    }

    private static int? TryReadPositiveInt(
        IReadOnlyDictionary<string, string> parameters,
        string key)
    {
        return parameters.TryGetValue(key, out string? value)
            && int.TryParse(value, out int parsed)
            && parsed > 0
                ? parsed
                : null;
    }
}
