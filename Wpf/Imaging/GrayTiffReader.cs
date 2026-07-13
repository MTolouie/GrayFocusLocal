using Wpf.Entities;
using System;
using BitMiracle.LibTiff.Classic;

namespace Wpf.Imaging
{
    /// <summary>
    /// Decodes grayscale TIFFs from disk into <see cref="GrayImage"/> using
    /// BitMiracle.LibTiff.NET (pure managed, New BSD license, free for
    /// commercial use) — no WPF dependency, no commercial license required.
    ///
    /// Mirrors the original ViewModel behavior:
    ///   - native 8-bit grayscale source  -> kept as Gray8
    ///   - native 16-bit grayscale source -> kept as Gray16
    ///   - anything else (color, palette, other bit depths) -> converted to Gray16
    /// </summary>
    public static class GrayTiffReader
    {
        public static GrayImage Read(string imagePath)
        {
            using Tiff tiff = Tiff.Open(imagePath, "r")
                ?? throw new InvalidOperationException($"Could not open TIFF file: {imagePath}");

            int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();

            FieldValue[] bitsField = tiff.GetField(TiffTag.BITSPERSAMPLE);
            FieldValue[] samplesField = tiff.GetField(TiffTag.SAMPLESPERPIXEL);
            FieldValue[] photometricField = tiff.GetField(TiffTag.PHOTOMETRIC);

            int bitsPerSample = bitsField != null ? bitsField[0].ToInt() : 8;
            int samplesPerPixel = samplesField != null ? samplesField[0].ToInt() : 1;

            // WPF's BitmapDecoder always normalized pixels to "0 = black,
            // max = white" regardless of the file's actual photometric tag.
            // LibTiff.NET hands us raw samples as-is, so if the source TIFF
            // is MinIsWhite we must invert here to match that same
            // convention and avoid silently shifting min/max stats.
            bool isMinIsWhite = photometricField != null
                && (Photometric)photometricField[0].ToInt() == Photometric.MINISWHITE;

            if (samplesPerPixel == 1 && bitsPerSample == 8)
            {
                return ReadGray8(tiff, width, height, isMinIsWhite);
            }

            if (samplesPerPixel == 1 && bitsPerSample == 16)
            {
                return ReadGray16(tiff, width, height, isMinIsWhite);
            }

            return ReadAsGray16Native(tiff, width, height);
        }

        private static GrayImage ReadGray8(Tiff tiff, int width, int height, bool isMinIsWhite)
        {
            var pixels = new byte[width * height];
            var scanline = new byte[tiff.ScanlineSize()];

            for (int y = 0; y < height; y++)
            {
                tiff.ReadScanline(scanline, y);
                Buffer.BlockCopy(scanline, 0, pixels, y * width, width);
            }

            if (isMinIsWhite)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = (byte)(255 - pixels[i]);
                }
            }

            return new GrayImage
            {
                Width = width,
                Height = height,
                PixelFormat = RoiPixelFormat.Gray8,
                Pixels8 = pixels,
                Pixels16 = null
            };
        }

        private static GrayImage ReadGray16(Tiff tiff, int width, int height, bool isMinIsWhite)
        {
            var pixels = new ushort[width * height];
            var scanline = new byte[tiff.ScanlineSize()];

            for (int y = 0; y < height; y++)
            {
                tiff.ReadScanline(scanline, y);
                Buffer.BlockCopy(scanline, 0, pixels, y * width * sizeof(ushort), width * sizeof(ushort));
            }

            if (isMinIsWhite)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = (ushort)(65535 - pixels[i]);
                }
            }

            return new GrayImage
            {
                Width = width,
                Height = height,
                PixelFormat = RoiPixelFormat.Gray16,
                Pixels16 = pixels,
                Pixels8 = null
            };
        }

        // Fallback for color/palette/unusual-bit-depth TIFFs: decode as RGBA
        // and convert to 16-bit luminance, mirroring the original
        // FormatConvertedBitmap(..., PixelFormats.Gray16, ...) fallback path.
        private static GrayImage ReadAsGray16Native(Tiff tiff, int width, int height)
        {
            var pixels = new ushort[width * height];
            int scanlineSize = tiff.ScanlineSize();

            for (int y = 0; y < height; y++)
            {
                byte[] scanlineBuffer = new byte[scanlineSize];
                tiff.ReadScanline(scanlineBuffer, y);

                int pixelRowOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    int byteIndex = x * 2;
                    if (byteIndex + 1 < scanlineSize)
                    {
                        // FIX ENDIANNESS: Combine the two bytes in Big-Endian order explicitly
                        ushort pixelValue = (ushort)((scanlineBuffer[byteIndex] << 8) | scanlineBuffer[byteIndex + 1]);
                        pixels[pixelRowOffset + x] = pixelValue;
                    }
                }
            }

            return new GrayImage
            {
                Width = width,
                Height = height,
                PixelFormat = RoiPixelFormat.Gray16,
                Pixels16 = pixels,
                Pixels8 = null
            };
        }
    }
}