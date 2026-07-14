using System;
using System.Globalization;
using System.Windows.Data;

namespace Wpf.Helpers
{
    public class DecimalTextBoxConverter : IValueConverter
    {
        // This keeps track of what the user is currently typing
        private string _lastValue = string.Empty;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            double dValue = (double)value;

            // If the user is currently typing a trailing dot (e.g. "50.") or zero (e.g. "50.0"),
            // and the double value matches, keep the user's visual text instead of forcing "50".
            if (!string.IsNullOrEmpty(_lastValue) && double.TryParse(_lastValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedLast))
            {
                if (Math.Abs(parsedLast - dValue) < double.Epsilon)
                {
                    return _lastValue;
                }
            }

            return dValue.ToString(CultureInfo.InvariantCulture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            if (string.IsNullOrWhiteSpace(strValue))
            {
                return 0.0;
            }

            _lastValue = strValue.Trim();

            // Handle typing dots smoothly
            if (_lastValue.EndsWith(".") || _lastValue.EndsWith(","))
            {
                string cleanVal = _lastValue.Substring(0, _lastValue.Length - 1);
                if (double.TryParse(cleanVal, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                    return result;
            }

            if (double.TryParse(_lastValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                return val;
            }

            return 0.0;
        }
    }
}