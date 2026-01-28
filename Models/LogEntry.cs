using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Models
{
    public class LogEntry
    {
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

        public string Summary
        {
            get
            {
                if (Fields == null || Fields.Count == 0)
                    return "";

                try
                {
                    var settings = ColumnSettingsManager.CurrentSettings;
                    
                    // 컨럼으로 표시되거나 숨김인 필드는 제외
                    var excludeFields = new HashSet<string>(
                        settings.Fields
                            .Where(f => f.DisplayType != FieldDisplayType.Summary)
                            .Select(f => f.FieldName)
                    );

                    // Summary로 설정된 필드만 표시
                    var summaryFields = Fields
                        .Where(f => !excludeFields.Contains(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                        .Take(8)
                        .Select(f => $"{f.Key}:{f.Value}");

                    var result = string.Join(" | ", summaryFields);
                    return string.IsNullOrEmpty(result) ? "-" : result;
                }
                catch
                {
                    // 설정 로드 실패 시 기본 동작
                    var otherFields = Fields
                        .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                        .Take(5)
                        .Select(f => $"{f.Key}:{f.Value}");
                    return string.Join(" | ", otherFields);
                }
            }
        }

        public Brush BackgroundBrush
        {
            get
            {
                if (Direction == "SEND")
                    return new SolidColorBrush(Color.FromRgb(230, 240, 255));
                else
                    return new SolidColorBrush(Color.FromRgb(230, 255, 230));
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
