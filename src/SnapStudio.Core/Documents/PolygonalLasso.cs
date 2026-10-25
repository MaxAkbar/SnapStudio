using System.Collections.ObjectModel;
using SnapStudio.Core.Primitives;

namespace SnapStudio.Core.Documents;

/// <summary>Collects committed corners; pointer movement only changes the UI preview.</summary>
public sealed class PolygonalLasso
{
    private readonly List<PointD> _vertices = [];
    private readonly ReadOnlyCollection<PointD> _readOnlyVertices;

    public PolygonalLasso() => _readOnlyVertices = _vertices.AsReadOnly();
    public IReadOnlyList<PointD> Vertices => _readOnlyVertices;
    public bool CanClose => _vertices.Count >= 3;

    /// <returns>True when clicking near the first corner closes an existing polygon.</returns>
    public bool Click(PointD point, double closingTolerance)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(closingTolerance) || closingTolerance < 0)
            throw new ArgumentException("Choose a finite point and a nonnegative closing tolerance.");
        if (CanClose && Distance(point, _vertices[0]) <= closingTolerance) return true;
        if (_vertices.Count > 0 && (Distance(point, _vertices[^1]) < 1e-6 || Distance(point, _vertices[0]) < 1e-6)) return false;
        _vertices.Add(point);
        return false;
    }

    public void RemoveLastCorner()
    {
        if (_vertices.Count > 0) _vertices.RemoveAt(_vertices.Count - 1);
    }

    public void Clear() => _vertices.Clear();

    private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
