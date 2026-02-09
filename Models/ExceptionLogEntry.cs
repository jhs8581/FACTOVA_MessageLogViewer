using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// EXCEPTION 로그 엔트리
    /// </summary>
    public class ExceptionLogEntry
    {
        /// <summary>
        /// 행 번호
        /// </summary>
        public int RowNumber { get; set; }

        /// <summary>
        /// 타임스탬프
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 시간 문자열 (HH:mm:ss.fff)
        /// </summary>
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// 예외 타입 (예: Exception)
        /// </summary>
        public string ExceptionType { get; set; } = "";

        /// <summary>
        /// 비즈명 (BR_SFC_xxx)
        /// </summary>
        public string BizName { get; set; } = "";

        /// <summary>
        /// 예외 메시지 (</NewDataSet> 이후 내용)
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// 소스 (발생 위치)
        /// </summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// 스택 트레이스
        /// </summary>
        public string StackTrace { get; set; } = "";

        /// <summary>
        /// 원본 데이터
        /// </summary>
        public string RawData { get; set; } = "";

        /// <summary>
        /// 동적 필드 (XML 파싱 결과)
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>
        /// 파라미터 문자열 (KEY=VALUE | KEY=VALUE 형태)
        /// </summary>
        public string ParameterString => Fields.Count > 0 
            ? string.Join(" | ", Fields.Select(f => $"{f.Key}={f.Value}"))
            : "";

        /// <summary>
        /// 요약 (첫 줄)
        /// </summary>
        public string Summary => Message.Length > 100 ? Message.Substring(0, 100) + "..." : Message;

        /// <summary>
        /// 행 배경색 (교차 색상)
        /// </summary>
        public Brush BackgroundBrush => RowNumber % 2 == 0 
            ? new SolidColorBrush(Color.FromRgb(255, 255, 255)) 
            : new SolidColorBrush(Color.FromRgb(255, 250, 250)); // 연한 빨강 계열
    }
}
