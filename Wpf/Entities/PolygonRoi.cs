namespace Wpf.Entities;

public sealed class PolygonRoi
{
    public IReadOnlyList<RoiPoint> Points { get; }

    public PolygonRoi(IReadOnlyList<RoiPoint> points)
    {
        Points = points;
    }
}