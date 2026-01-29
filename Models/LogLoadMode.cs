namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 로그 로드 모드
    /// </summary>
    public enum LogLoadMode
    {
        /// <summary>
        /// 실행 시점 이후 로그만 표시 (빠른 시작)
        /// </summary>
        NewOnly,

        /// <summary>
        /// 최근 N개 로드
        /// </summary>
        Recent,

        /// <summary>
        /// 전체 로그 로드
        /// </summary>
        All
    }
}
