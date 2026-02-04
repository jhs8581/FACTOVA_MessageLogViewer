namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 로그 파일 타입
    /// </summary>
    public enum LogType
    {
        /// <summary>
        /// EVENT 로그 - 통신 이벤트 로그
        /// </summary>
        EVENT,

        /// <summary>
        /// DATA 로그 - 서비스 실행 로그
        /// </summary>
        DATA,

        /// <summary>
        /// DEBUG 로그 - 디버그 정보
        /// </summary>
        DEBUG,

        /// <summary>
        /// EXCEPTION 로그 - 예외 정보
        /// </summary>
        EXCEPTION
    }
}
