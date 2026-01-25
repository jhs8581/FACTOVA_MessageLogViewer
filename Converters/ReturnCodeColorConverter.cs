using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Converters
{
    public class ReturnCodeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string returnCode = value?.ToString() ?? "";

            if (string.IsNullOrEmpty(returnCode))
                return new SolidColorBrush(Colors.Transparent);

            switch (returnCode.ToUpper())
            {
                case "OK":
                case "PASS":
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80));

                case "NG":
                case "FAIL":
                case "ERROR":
                    return new SolidColorBrush(Color.FromRgb(244, 67, 54));

                default:
                    return new SolidColorBrush(Color.FromRgb(158, 158, 158));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}