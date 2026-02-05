using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Converters
{
    /// <summary>
    /// F/L 로그의 Value를 ON/OFF 색상으로 변환
    /// </summary>
    public class FLValueColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush OnBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // Green
        private static readonly SolidColorBrush OffBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
        private static readonly SolidColorBrush DefaultBrush = new SolidColorBrush(Color.FromRgb(33, 33, 33)); // Black

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string strValue)
            {
                if (strValue.Equals("ON", StringComparison.OrdinalIgnoreCase))
                    return OnBrush;
                if (strValue.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                    return OffBrush;
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
