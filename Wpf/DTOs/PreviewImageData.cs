using System;

namespace Wpf.DTOs
{
    /// <summary>
    /// Raw BGR24 pixel buffer for a single preview image, pulled directly out
    /// of the numpy ndarray that grayscale_clr.py's get_image() returns.
    /// There's no file and no URL anymore — ResultViewModel decodes this
    /// straight into a BitmapSource in memory.
    /// </summary>
    public class PreviewImageData
    {
        public byte[] PixelData { get; set; } = Array.Empty<byte>();
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
    }
}