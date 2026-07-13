using Wpf.Entities;

 public sealed class GrayImage
{
    public int Width { get; init; }

    public int Height { get; init; }

    public RoiPixelFormat PixelFormat { get; init; }

    public ushort[] Pixels16 { get; init; }

    public byte[] Pixels8 { get; init; }
}