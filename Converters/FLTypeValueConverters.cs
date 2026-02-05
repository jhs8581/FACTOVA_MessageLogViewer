using FACTOVA_MessageLogViewer.Models;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Converters
{
    /// <summary>
    /// F/L Boolean 값을 ON/OFF로 변환하는 컨버터
    /// Boolean 타입이 아니면 빈 문자열 반환
    /// </summary>
    public class FLBooleanValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FLLogEntry entry)
            {
                // Boolean 타입만 표시
                if (entry.DataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                {
                    return entry.IsOn ? "ON" : "OFF";
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// F/L Structure 값만 표시하는 컨버터
    /// Structure 타입이 아니면 빈 문자열 반환
    /// </summary>
    public class FLStructureValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FLLogEntry entry)
            {
                // Structure 타입만 표시
                if (entry.IsStructure)
                {
                    return entry.DisplayValue;
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
