using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace FACTOVA_MessageLogViewer.Converters
{
    /// <summary>
    /// F/L Structure 필드 값을 표시값으로 변환하는 컨버터
    /// </summary>
    public class FLFieldValueConverter : IValueConverter
    {
        private readonly FLFieldConfig _fieldConfig;

        public FLFieldValueConverter(FLFieldConfig fieldConfig)
        {
            _fieldConfig = fieldConfig;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value는 Fields 딕셔너리
            if (value is Dictionary<string, string> fields)
            {
                if (fields.TryGetValue(_fieldConfig.FieldName, out var fieldValue))
                {
                    // 값 매핑 적용
                    return _fieldConfig.GetDisplayValue(fieldValue);
                }
                return ""; // 키가 없으면 빈 문자열
            }

            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

