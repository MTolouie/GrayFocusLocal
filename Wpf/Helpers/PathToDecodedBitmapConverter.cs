using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Wpf.Helpers
{
    // Converts an image file path into a BitmapImage decoded at a capped
    // resolution instead of full size. WPF's default string->ImageSource
    // conversion decodes the source file at FULL resolution on the UI
    // thread every single time the bound path changes. Since ImagePath is
    // reassigned once per processed slice during a batch run, that means a
    // full-res decode per image — this is almost certainly your real
    // bottleneck, not the string property updates.
    public class PathToDecodedBitmapConverter : IValueConverter
    {
        // Tune this to the largest width the image is actually displayed at.
        public int DecodePixelWidth { get; set; } = 1200;

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = DecodePixelWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            return bmp;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}