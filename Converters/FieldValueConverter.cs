using FACTOVA_MessageLogViewer.Models;
using System;
using System.Globalization;
using System.Windows.Data;

namespace FACTOVA_MessageLogViewer.Converters
{
    /// <summary>
    /// 필드 값을 Display Mapping에 따라 변환하는 컨버터
    /// </summary>
    public class FieldValueConverter : IValueConverter
    {
        public FieldConfig? Config { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || Config == null)
                return value ?? "";

            var stringValue = value.ToString() ?? "";
            var result = Config.GetDisplayValue(stringValue);
            
            System.Diagnostics.Debug.WriteLine($"FieldValueConverter - Field: {Config.FieldName}, Input: '{stringValue}', Mapping: '{Config.ValueMapping}', Output: '{result}'");
            
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

