using SnapStudio.Core.Documents;

namespace SnapStudio.Core.Tests;

[TestClass]
public sealed class AnnotationPropertiesTests
{
    [TestMethod]
    [DataRow(AnnotationKind.Rectangle, AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity | AnnotationProperty.Corners | AnnotationProperty.ShapeFill)]
    [DataRow(AnnotationKind.Ellipse, AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity | AnnotationProperty.ShapeFill)]
    [DataRow(AnnotationKind.Line, AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Arrow, AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Pen, AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Text, AnnotationProperty.Color | AnnotationProperty.TextSize | AnnotationProperty.Opacity | AnnotationProperty.Font | AnnotationProperty.TextContent)]
    [DataRow(AnnotationKind.Highlight, AnnotationProperty.Color | AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Fill, AnnotationProperty.Color | AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Image, AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Vector, AnnotationProperty.Opacity)]
    [DataRow(AnnotationKind.Blur, AnnotationProperty.BlurStrength)]
    public void For_ExposesOnlyPropertiesUsedByEachKind(AnnotationKind kind, AnnotationProperty expected)
    {
        Assert.AreEqual(expected, AnnotationProperties.For(kind));
    }

    [TestMethod]
    public void Common_NoObjectsOrUnknownKindHasNoAppearanceProperties()
    {
        Assert.AreEqual(AnnotationProperty.None, AnnotationProperties.Common([]));
        Assert.AreEqual(AnnotationProperty.None, AnnotationProperties.For((AnnotationKind)999));
    }

    [TestMethod]
    public void Common_RectangleAndEllipseExcludeRectangleCorners()
    {
        Assert.AreEqual(AnnotationProperty.Color | AnnotationProperty.StrokeWidth | AnnotationProperty.Opacity | AnnotationProperty.ShapeFill,
            AnnotationProperties.Common([AnnotationKind.Rectangle, AnnotationKind.Ellipse]));
    }

    [TestMethod]
    public void Common_TextAndShapeDoNotShareSizeFontOrFill()
    {
        Assert.AreEqual(AnnotationProperty.Color | AnnotationProperty.Opacity,
            AnnotationProperties.Common([AnnotationKind.Text, AnnotationKind.Rectangle]));
    }

    [TestMethod]
    public void Common_ImageAndOtherObjectsShareOnlyOpacity()
    {
        Assert.AreEqual(AnnotationProperty.Opacity,
            AnnotationProperties.Common([AnnotationKind.Image, AnnotationKind.Text, AnnotationKind.Pen]));
    }

    [TestMethod]
    public void Common_BlurStrengthIsNotStrokeWidthOrObjectOpacity()
    {
        Assert.AreEqual(AnnotationProperty.None, AnnotationProperties.Common([AnnotationKind.Blur, AnnotationKind.Rectangle]));
        Assert.AreEqual(AnnotationProperty.BlurStrength, AnnotationProperties.Common([AnnotationKind.Blur, AnnotationKind.Blur]));
    }
}
