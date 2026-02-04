using System;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 로그 뷰어 설정 파라미터 (메인 화면에서 각 탭으로 전달)
    /// </summary>
    public record LogViewerSettings
    {
        /// <summary>
        /// 로그 파일 경로
        /// </summary>
        public string LogFilePath { get; init; } = "";

        /// <summary>
        /// 선택된 날짜
        /// </summary>
        public DateTime SelectedDate { get; init; } = DateTime.Today;

        /// <summary>
        /// 로그 로드 모드
        /// </summary>
        public LogLoadMode LoadMode { get; init; } = LogLoadMode.NewOnly;

        /// <summary>
        /// 최근 로그 개수 (LoadMode.Recent일 때 사용)
        /// </summary>
        public int RecentCount { get; init; } = 1000;

        /// <summary>
        /// 시간 필터 시작
        /// </summary>
        public TimeSpan FilterStartTime { get; init; } = TimeSpan.Zero;

        /// <summary>
        /// 시간 필터 끝
        /// </summary>
        public TimeSpan FilterEndTime { get; init; } = new TimeSpan(23, 59, 59);

        /// <summary>
        /// 느린 쿼리만 표시 (DATA 로그용)
        /// </summary>
        public bool SlowQueryOnly { get; init; } = false;

        /// <summary>
        /// 로그 타입
        /// </summary>
        public LogType LogType { get; init; } = LogType.EVENT;

        /// <summary>
                /// 로그 폴더 경로
                /// </summary>
                public string LogDirectory { get; init; } = "";

                /// <summary>
                /// 기본 폴더 여부
                /// </summary>
                public bool IsDefaultFolder { get; init; } = true;

                /// <summary>
                /// 실시간 감지 여부 (FileWatcher 활성화)
                /// </summary>
                public bool EnableRealTimeWatch { get; init; } = true;
            }
        }
