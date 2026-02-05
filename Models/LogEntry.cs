using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// WPF 바인딩 시 키가 없어도 예외를 발생시키지 않는 안전한 딕셔너리 래퍼
    /// </summary>
    public class SafeFieldsDictionary : IEnumerable<KeyValuePair<string, string>>
    {
        private readonly Dictionary<string, string> _inner;

        public SafeFieldsDictionary(Dictionary<string, string>? dict = null)
        {
            _inner = dict ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// 안전한 인덱서 - 키가 없으면 빈 문자열 반환 (예외 없음)
        /// </summary>
        public string this[string key]
        {
            get => _inner.TryGetValue(key, out var value) ? value : "";
            set => _inner[key] = value;
        }

        public int Count => _inner.Count;
        
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        
        public bool TryGetValue(string key, out string value) => _inner.TryGetValue(key, out value!);
        
        public ICollection<string> Keys => _inner.Keys;
        
        public ICollection<string> Values => _inner.Values;

        public void Add(string key, string value) => _inner[key] = value;
        
        public Dictionary<string, string> ToDictionary() => new Dictionary<string, string>(_inner);

        public string GetValueOrDefault(string key, string defaultValue = "") 
            => _inner.TryGetValue(key, out var value) ? value : defaultValue;

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _inner.GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();

        // Dictionary<string, string>에서 SafeFieldsDictionary로 암시적 변환
        public static implicit operator SafeFieldsDictionary(Dictionary<string, string>? dict) 
            => new SafeFieldsDictionary(dict);
    }

    public class LogEntry
    {
        public int RowNumber { get; set; }  // ROW 번호 추가
        public DateTime Timestamp { get; set; }
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
        public string Direction { get; set; } = "";
        public string DirectionText => Direction == "SEND" ? "송신" : (Direction == "INFO" ? "정보" : "수신");
        public string MessageId { get; set; } = "";
        
        private Dictionary<string, string> _fields = new Dictionary<string, string>();
        
        /// <summary>
        /// 필드 딕셔너리 - 안전한 인덱서로 접근 가능
        /// </summary>
        public SafeFieldsDictionary Fields 
        { 
            get => new SafeFieldsDictionary(_fields);
            set => _fields = value?.ToDictionary() ?? new Dictionary<string, string>();
        }
        
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
                    // 키워드 기반 로그 (CONTENT 필드가 있고 MSGID가 없는 경우)
                    // → 원본 텍스트 그대로 표시
                    if (string.IsNullOrEmpty(MessageId) && Fields.TryGetValue("CONTENT", out var contentValue))
                    {
                        _cachedSummary = contentValue;
                        return _cachedSummary;
                    }

                    var settings = ColumnSettingsManager.CurrentSettings;
                    
                    // 컨럼으로 표시되거나 숨김인 필드는 제외
                    var excludeFields = new HashSet<string>(
                        settings.Fields
                            .Where(f => f.DisplayType != FieldDisplayType.Summary)
                            .Select(f => f.FieldName)
                    );

                    // Summary로 설정된 필드 모두 표시 (알파벳 정렬)
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
                    // 설정 로드 실패 시 기본 동작
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
