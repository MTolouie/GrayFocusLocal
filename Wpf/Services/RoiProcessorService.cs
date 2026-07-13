using Wpf.Services.IService;
using Wpf.Entities;
using Wpf.Imaging;
using System;
using System.Threading.Tasks;

namespace Wpf.Services
{
    public class RoiProcessorService : IRoiProcessorService
    {
        public Task<RoiResult> CreateRectangleAsync(string imagePath, RectangleRoi rectangle)
        {
            return Task.Run(() =>
            {
                GrayImage source = GrayTiffReader.Read(imagePath);

                int x0 = Math.Max(0, (int)Math.Floor(rectangle.X));
                int y0 = Math.Max(0, (int)Math.Floor(rectangle.Y));
                int x1 = Math.Min(source.Width, (int)Math.Ceiling(rectangle.X + rectangle.Width));
                int y1 = Math.Min(source.Height, (int)Math.Ceiling(rectangle.Y + rectangle.Height));

                int width = Math.Max(0, x1 - x0);
                int height = Math.Max(0, y1 - y0);

                return source.PixelFormat == RoiPixelFormat.Gray16
                    ? BuildResult16(source, x0, y0, width, height, mask: null, RoiShape.Rectangle)
                    : BuildResult8(source, x0, y0, width, height, mask: null, RoiShape.Rectangle);
            });
        }

        public Task<RoiResult> CreatePolygonAsync(string imagePath, PolygonRoi polygon)
        {
            return Task.Run(() =>
            {
                GrayImage source = GrayTiffReader.Read(imagePath);

                if (polygon.Points.Count < 3)
                {
                    return source.PixelFormat == RoiPixelFormat.Gray16
                        ? BuildResult16(source, 0, 0, 0, 0, mask: null, RoiShape.Polygon)
                        : BuildResult8(source, 0, 0, 0, 0, mask: null, RoiShape.Polygon);
                }

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                foreach (var p in polygon.Points)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }

                int x0 = Math.Max(0, (int)Math.Floor(minX));
                int y0 = Math.Max(0, (int)Math.Floor(minY));
                int x1 = Math.Min(source.Width, (int)Math.Ceiling(maxX));
                int y1 = Math.Min(source.Height, (int)Math.Ceiling(maxY));

                int width = Math.Max(0, x1 - x0);
                int height = Math.Max(0, y1 - y0);

                bool[] mask = BuildPolygonMask(polygon, x0, y0, width, height);

                return source.PixelFormat == RoiPixelFormat.Gray16
                    ? BuildResult16(source, x0, y0, width, height, mask, RoiShape.Polygon)
                    : BuildResult8(source, x0, y0, width, height, mask, RoiShape.Polygon);
            });
        }

        // --- Polygon mask fill (cv2.fillPoly equivalent) ---
        // Same odd-even point-in-polygon test the ViewModel used, sampled at
        // pixel centers (x+0.5, y+0.5), applied only within the polygon's
        // bounding box so the resulting mask/crop matches
        // cv2.fillPoly(mask, ...) followed by cropping to the bounding rect.
        private static bool[] BuildPolygonMask(PolygonRoi polygon, int originX, int originY, int width, int height)
        {
            var mask = new bool[Math.Max(0, width * height)];
            int count = polygon.Points.Count;

            var polyX = new double[count];
            var polyY = new double[count];

            for (int i = 0; i < count; i++)
            {
                polyX[i] = polygon.Points[i].X;
                polyY[i] = polygon.Points[i].Y;
            }

            for (int y = 0; y < height; y++)
            {
                double sampleY = originY + y + 0.5;
                int rowOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    double sampleX = originX + x + 0.5;

                    if (IsPointInPolygon(sampleX, sampleY, polyX, polyY, count))
                    {
                        mask[rowOffset + x] = true;
                    }
                }
            }

            return mask;
        }

        private static bool IsPointInPolygon(double x, double y, double[] polyX, double[] polyY, int count)
        {
            bool inside = false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polyY[i] > y) != (polyY[j] > y)) &&
                    (polyY[j] - polyY[i] != 0) &&
                    (x < (polyX[j] - polyX[i]) * (y - polyY[i]) / (polyY[j] - polyY[i]) + polyX[i]))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private RoiResult BuildResult16(GrayImage source, int originX, int originY, int width, int height, bool[] mask, RoiShape shape)
        {
            ushort[] cropped = new ushort[Math.Max(0, width * height)];
            ushort min = ushort.MaxValue;
            ushort max = ushort.MinValue;
            bool found = false;

            for (int y = 0; y < height; y++)
            {
                int targetY = originY + y;
                if (targetY < 0 || targetY >= source.Height) continue;

                int srcRow = targetY * source.Width;
                int dstRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    int targetX = originX + x;
                    if (targetX < 0 || targetX >= source.Width) continue;

                    bool included = mask == null || mask[dstRow + x];

                    if (included)
                    {
                        ushort value = source.Pixels16![srcRow + targetX];
                        cropped[dstRow + x] = value;

                        // Rectangle mode background padding edge fix for 16-bit
                        if (mask == null && value == (ushort)0)
                        {
                            continue;
                        }

                        if (value < min) min = value;
                        if (value > max) max = value;
                        found = true;
                    }
                    else
                    {
                        cropped[dstRow + x] = 0;
                    }
                }
            }

            byte[] encoded = width > 0 && height > 0
                ? GrayTiffWriter.EncodeGray16(cropped, width, height)
                : Array.Empty<byte>();

            return new RoiResult
            {
                Shape = shape,
                Width = width,
                Height = height,
                PixelFormat = RoiPixelFormat.Gray16,
                Pixels8 = null,
                Pixels16 = cropped,
                HasPixels = found,
                MinValue = found ? min : (ushort)0,
                MaxValue = found ? max : (ushort)0,
                EncodedTiffBytes = encoded
            };
        }

        private RoiResult BuildResult8(GrayImage source, int originX, int originY, int width, int height, bool[] mask, RoiShape shape)
        {
            byte[] cropped = new byte[Math.Max(0, width * height)];
            byte min = byte.MaxValue;
            byte max = byte.MinValue;
            bool found = false;

            for (int y = 0; y < height; y++)
            {
                int targetY = originY + y;
                if (targetY < 0 || targetY >= source.Height) continue;

                int srcRow = targetY * source.Width;
                int dstRow = y * width;

                for (int x = 0; x < width; x++)
                {
                    int targetX = originX + x;
                    if (targetX < 0 || targetX >= source.Width) continue;

                    bool included = mask == null || mask[dstRow + x];

                    if (included)
                    {
                        byte value = source.Pixels8![srcRow + targetX];
                        cropped[dstRow + x] = value;

                        // Rectangle mode background padding edge fix for 8-bit
                        if (mask == null && value == (byte)0)
                        {
                            continue;
                        }

                        if (value < min) min = value;
                        if (value > max) max = value;
                        found = true;
                    }
                    else
                    {
                        cropped[dstRow + x] = 0;
                    }
                }
            }

            byte[] encoded = width > 0 && height > 0
                ? GrayTiffWriter.EncodeGray8(cropped, width, height)
                : Array.Empty<byte>();

            return new RoiResult
            {
                Shape = shape,
                Width = width,
                Height = height,
                PixelFormat = RoiPixelFormat.Gray8,
                Pixels8 = cropped,
                Pixels16 = null,
                HasPixels = found,
                MinValue = found ? min : (byte)0,
                MaxValue = found ? max : (byte)0,
                EncodedTiffBytes = encoded
            };
        }
    }
}