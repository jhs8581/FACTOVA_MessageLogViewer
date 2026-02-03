using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Models
{
    public class LogEntry
    {
        public int RowNumber { get; set; }  // ROW 번호 추가
        public DateTime Timestamp { get; set; }
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
        public string Direction { get; set; } = "";
        public string DirectionText => Direction == "SEND" ? "송신" : "수신";
        public string MessageId { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
        public string RawData { get; set; } = "";

        /// <summary>
        /// 매칭된 탭 이름 (통합 로그 제외, 첫 번째 매칭 탭)
        /// </summary>
        public string MatchedTabName { get; set; } = "";

        // 직접 설정 가능한 필드들
        private string? _returnCode;
        private string? _workType;
        private string? _errorCode;

        public string ReturnCode
        {
            get => _returnCode ?? GetFieldValue("RETURN_CODE");
            set => _returnCode = value;
        }

        public string WorkType
        {
            get => _workType ?? GetFieldValue("WORK_TYPE");
            set => _workType = value;
        }

        public string ErrorCode
        {
            get => _errorCode ?? GetFieldValue("ERROR_CODE");
            set => _errorCode = value;
        }




        public string LotId => GetFieldValue("LOTID");
        public string PalletId => GetFieldValue("PALLET_ID", "DOOR_PALLET_ID");

        private string? _cachedSummary = null;
        public string Summary
        {
            get
            {
                // 캐싱으로 성능 최적화
                if (_cachedSummary != null)
                    return _cachedSummary;

                if (Fields == null || Fields.Count == 0)
                {
                    _cachedSummary = "";
                    return _cachedSummary;
                }

                try
                {
                    var settings = ColumnSettingsManager.CurrentSettings;
                    
                    // 컨럼으로 표시되거나 숨김인 필드는 제외
                    var excludeFields = new HashSet<string>(
                        settings.Fields
                            .Where(f => f.DisplayType != FieldDisplayType.Summary)
                            .Select(f => f.FieldName)
                    );

                    // Summary로 설정된 필드 모두 표시 (알파벳 정렬, 길이 제한 적용)
                    var summaryFields = Fields
                        .Where(f => !excludeFields.Contains(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                        .OrderBy(f => f.Key)
                        .Select(f => $"{f.Key}:{f.Value}");

                    var result = string.Join(" | ", summaryFields);
                    
                    // 최대 길이 제한 (150자로 축소)
                    if (result.Length > 150)
                        result = result.Substring(0, 150);
                    
                    _cachedSummary = string.IsNullOrEmpty(result) ? "-" : result;
                    return _cachedSummary;
                }
                catch
                {
                    // 설정 로드 실패 시 기본 동작
                    var otherFields = Fields
                        .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                        .OrderBy(f => f.Key)
                        .Select(f => $"{f.Key}:{f.Value}");
                    
                    var result = string.Join(" | ", otherFields);
                    if (result.Length > 150)
                        result = result.Substring(0, 150);
                    
                    _cachedSummary = result;
                    return _cachedSummary;
                }
            }
        }

        private Brush? _cachedBackgroundBrush = null;
        public Brush BackgroundBrush
        {
            get
            {
                // 캐싱으로 성능 최적화
                if (_cachedBackgroundBrush != null)
                    return _cachedBackgroundBrush;

                if (Direction == "SEND")
                    _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(230, 240, 255));
                else
                    _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(230, 255, 230));

                _cachedBackgroundBrush.Freeze();  // Freeze로 성능 향상
                return _cachedBackgroundBrush;
            }
        }

        private string GetFieldValue(params string[] keys)
        {
            if (Fields == null)
                return "";

            foreach (var key in keys)
            {
                if (Fields.ContainsKey(key) && !string.IsNullOrWhiteSpace(Fields[key]))
                    return Fields[key];
            }

            return "";
        }
    }
}
