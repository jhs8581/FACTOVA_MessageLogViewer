using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace FACTOVA_MessageLogViewer.Converters
{
    /// <summary>
    /// 값 변환 매핑 컨버터
    /// 예: "1=ON,0=OFF,Y=사용,N=미사용"
    /// </summary>
    public class ValueMappingConverter : IValueConverter
    {
        private readonly Dictionary<string, string> mappings = new(StringComparer.OrdinalIgnoreCase);

        public ValueMappingConverter(string mappingString)
        {
            ParseMapping(mappingString);
        }

        private void ParseMapping(string mappingString)
        {
            if (string.IsNullOrWhiteSpace(mappingString)) return;

            var pairs = mappingString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        mappings[key] = value;
                    }
                }
            }
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return null;

            var strValue = value.ToString() ?? "";
            
            if (mappings.TryGetValue(strValue, out var mapped))
            {
                return mapped;
            }

            return strValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
