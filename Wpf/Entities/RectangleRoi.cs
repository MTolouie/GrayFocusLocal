namespace Wpf.Entities;

public sealed class RectangleRoi
{
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public RectangleRoi(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}