namespace Wpf.Entities;

public sealed class RoiResult
{
    public RoiShape Shape { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    public RoiPixelFormat PixelFormat { get; init; }

    public ushort[]? Pixels16 { get; init; }

    public byte[]? Pixels8 { get; init; }

    public bool HasPixels { get; init; }

    public int MinValue { get; init; }
    public int MaxValue { get; init; }

    public byte[] EncodedTiffBytes { get; init; } = System.Array.Empty<byte>();
}