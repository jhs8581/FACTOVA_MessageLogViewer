using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Converters
{
    public class ErrorColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string errorCode = value?.ToString() ?? "";

            if (string.IsNullOrEmpty(errorCode))
                return new SolidColorBrush(Color.FromRgb(158, 158, 158));

            return new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}