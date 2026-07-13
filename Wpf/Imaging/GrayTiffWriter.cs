using BitMiracle.LibTiff.Classic;
using System;
using System.IO;

namespace Wpf.Imaging
{
    /// <summary>
    /// Encodes raw grayscale pixel buffers back to TIFF bytes using
    /// BitMiracle.LibTiff.NET, so the ROI image that statistics were
    /// computed from is the exact same image bytes sent to the scanning
    /// API later.
    /// </summary>
    public static class GrayTiffWriter
    {
        public static byte[] EncodeGray16(ushort[] pixels, int width, int height)
        {
            return Encode(width, height, bitsPerSample: 16, (tiff, buffer, y) =>
            {
                Buffer.BlockCopy(pixels, y * width * sizeof(ushort), buffer, 0, width * sizeof(ushort));
                tiff.WriteScanline(buffer, y);
            });
        }

        public static byte[] EncodeGray8(byte[] pixels, int width, int height)
        {
            return Encode(width, height, bitsPerSample: 8, (tiff, buffer, y) =>
            {
                Buffer.BlockCopy(pixels, y * width, buffer, 0, width);
                tiff.WriteScanline(buffer, y);
            });
        }

        private static byte[] Encode(int width, int height, int bitsPerSample, Action<Tiff, byte[], int> writeRow)
        {
            using var stream = new MemoryStream();

            using (Tiff tiff = Tiff.ClientOpen("in-memory", "w", stream, new TiffStream()))
            {
                if (tiff == null)
                    throw new InvalidOperationException("Failed to create in-memory TIFF for encoding.");

                tiff.SetField(TiffTag.IMAGEWIDTH, width);
                tiff.SetField(TiffTag.IMAGELENGTH, height);
                tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
                tiff.SetField(TiffTag.BITSPERSAMPLE, bitsPerSample);
                tiff.SetField(TiffTag.SAMPLEFORMAT, SampleFormat.UINT);
                tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
                tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                tiff.SetField(TiffTag.ROWSPERSTRIP, height);
                tiff.SetField(TiffTag.COMPRESSION, Compression.NONE);
                tiff.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.NONE);

                byte[] buffer = new byte[width * (bitsPerSample / 8)];

                for (int y = 0; y < height; y++)
                {
                    writeRow(tiff, buffer, y);
                }
            }

            return stream.ToArray();
        }
    }
}