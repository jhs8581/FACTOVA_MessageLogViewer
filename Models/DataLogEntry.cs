using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// DATA 로그 엔트리
    /// 형식: [MM-DD-YYYY HH:mm:ss.fff] ExecuteService():[ BIZ_NAME ] / exec.Time : HH:mm:ss.fffffff / TXN_ID : xxx : Parameter : <NewDataSet>...</NewDataSet>
    /// </summary>
    public class DataLogEntry
    {
        public int RowNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
        
        /// <summary>
        /// 비즈명 (예: DA_COM_SEL_SERVERTIME, DA_CUS_SEL_SFC_PROGRAM_CONFIG_INFO)
        /// </summary>
        public string BizName { get; set; } = "";
        
        /// <summary>
        /// 실행 시간 (exec.Time)
        /// </summary>
        public string ExecTime { get; set; } = "";
        
        /// <summary>
        /// 트랜잭션 ID
        /// </summary>
        public string TxnId { get; set; } = "";
        
        /// <summary>
        /// 전체 파라미터 XML
        /// </summary>
        public string ParameterXml { get; set; } = "";
        
        /// <summary>
        /// 파싱된 파라미터 필드들
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
        
        /// <summary>
        /// 원본 데이터
        /// </summary>
        public string RawData { get; set; } = "";

        /// <summary>
        /// 매칭된 탭 이름
        /// </summary>
        public string MatchedTabName { get; set; } = "";

        // 주요 필드 접근자
        public string ClientId => GetFieldValue("CLIENT_ID");
        public string ClientIp => GetFieldValue("CLIENT_IP");
        public string ClientTime => GetFieldValue("CLIENT_TIME");
        public string EquipmentId => GetFieldValue("EQUIPMENT_ID");
        public string LangId => GetFieldValue("LANG_ID");
        public string SfcMode => GetFieldValue("SFC_MODE");

        private string? _cachedSummary = null;
        public string Summary
        {
            get
            {
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
                    
                    // 컬럼으로 표시되거나 숨김인 필드는 제외
                    var excludeFields = new HashSet<string>(
                        settings.DataFields
                            .Where(f => f.DisplayType != FieldDisplayType.Summary)
                            .Select(f => f.FieldName)
                    );

                    // Summary로 설정된 필드 모두 표시
                    var summaryFields = Fields
                        .Where(f => !excludeFields.Contains(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
                        .OrderBy(f => f.Key)
                        .Select(f => $"{f.Key}:{f.Value}");

                    var result = string.Join(" | ", summaryFields);
                    
                    _cachedSummary = string.IsNullOrEmpty(result) ? "-" : result;
                    return _cachedSummary;
                }
                catch
                {
                    var otherFields = Fields
                        .Where(f => !string.IsNullOrWhiteSpace(f.Value))
                        .OrderBy(f => f.Key)
                        .Select(f => $"{f.Key}:{f.Value}");
                    
                    var result = string.Join(" | ", otherFields);
                    
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
                if (_cachedBackgroundBrush != null)
                    return _cachedBackgroundBrush;

                // 실행시간 기준으로 색상 구분 (느린 쿼리 강조)
                if (TryParseExecTime(out var execTimeSpan))
                {
                    if (execTimeSpan.TotalSeconds >= 1.0)
                        _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 200, 200)); // 빨간색 (1초 이상)
                    else if (execTimeSpan.TotalMilliseconds >= 500)
                        _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 240, 200)); // 주황색 (500ms 이상)
                    else if (execTimeSpan.TotalMilliseconds >= 100)
                        _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 255, 220)); // 노란색 (100ms 이상)
                    else
                        _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(230, 255, 230)); // 녹색 (빠름)
                }
                else
                {
                    _cachedBackgroundBrush = new SolidColorBrush(Color.FromRgb(245, 245, 245)); // 기본
                }

                _cachedBackgroundBrush.Freeze();
                return _cachedBackgroundBrush;
            }
        }

        /// <summary>
        /// 실행시간을 TimeSpan으로 파싱
        /// </summary>
        public bool TryParseExecTime(out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(ExecTime))
                return false;

            return TimeSpan.TryParse(ExecTime, out result);
        }

        /// <summary>
        /// 실행시간을 밀리초로 반환
        /// </summary>
        public double ExecTimeMs
        {
            get
            {
                if (TryParseExecTime(out var ts))
                    return ts.TotalMilliseconds;
                return 0;
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
