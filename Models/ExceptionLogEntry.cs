using System;
using System.Collections.Generic;

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
        /// 시간 문자열 (HH:mm:ss)
        /// </summary>
        public string TimeString => Timestamp.ToString("HH:mm:ss");

        /// <summary>
        /// 예외 타입 (예: System.NullReferenceException)
        /// </summary>
        public string ExceptionType { get; set; } = "";

        /// <summary>
        /// 예외 메시지
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
        /// 요약 (첫 줄)
        /// </summary>
        public string Summary => Message.Length > 100 ? Message.Substring(0, 100) + "..." : Message;
    }
}
