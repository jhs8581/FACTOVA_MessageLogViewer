using System.Collections.Generic;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// DATA 로그용 컬럼 설정
    /// </summary>
    public class DataColumnSettings
    {
        /// <summary>
        /// 프리셋 이름
        /// </summary>
        public string Name { get; set; } = "Default";

        /// <summary>
        /// 폰트 크기
        /// </summary>
        public int FontSize { get; set; } = 11;

        /// <summary>
        /// 컬럼 필드 목록
        /// </summary>
        public List<DataFieldConfig> ColumnFields { get; set; } = new();

        /// <summary>
        /// 느린 쿼리 기본 임계값 (ms)
        /// </summary>
        public int DefaultSlowThreshold { get; set; } = 100;


        /// <summary>
        /// 기본 설정 생성
        /// </summary>
        public static DataColumnSettings CreateDefault()
        {
            return new DataColumnSettings
            {
                Name = "Default",
                FontSize = 11,
                DefaultSlowThreshold = 100,
                ColumnFields = new List<DataFieldConfig>
                {
                    new DataFieldConfig { Order = 1, FieldName = "RowNumber", DisplayName = "#", ColumnWidth = 50, IsEnabled = true },
                    new DataFieldConfig { Order = 2, FieldName = "TimeString", DisplayName = "시간", ColumnWidth = 90, IsEnabled = true },
                    new DataFieldConfig { Order = 10, FieldName = "BizName", DisplayName = "비즈명", ColumnWidth = 280, IsEnabled = true },
                    // 파라미터 컬럼이 여기에 삽입됨 (Order 100~899)
                    new DataFieldConfig { Order = 900, FieldName = "ExecTime", DisplayName = "실행시간", ColumnWidth = 100, IsEnabled = true },
                    new DataFieldConfig { Order = 901, FieldName = "TxnId", DisplayName = "TXN_ID", ColumnWidth = 180, IsEnabled = true },
                    new DataFieldConfig { Order = 902, FieldName = "ClientId", DisplayName = "CLIENT_ID", ColumnWidth = 100, IsEnabled = false },
                    new DataFieldConfig { Order = 903, FieldName = "ClientIp", DisplayName = "CLIENT_IP", ColumnWidth = 100, IsEnabled = false },
                    new DataFieldConfig { Order = 999, FieldName = "Summary", DisplayName = "파라미터", ColumnWidth = 0, IsEnabled = true }, // 0 = Star, 항상 마지막
                }
            };
        }

        /// <summary>
        /// EXCEPTION 로그용 기본 설정 생성
        /// </summary>
        public static DataColumnSettings CreateExceptionDefault()
        {
            return new DataColumnSettings
            {
                Name = "Default",
                FontSize = 11,
                DefaultSlowThreshold = 0,
                ColumnFields = new List<DataFieldConfig>
                {
                    new DataFieldConfig { Order = 1, FieldName = "RowNumber", DisplayName = "#", ColumnWidth = 50, IsEnabled = true },
                    new DataFieldConfig { Order = 2, FieldName = "TimeString", DisplayName = "시간", ColumnWidth = 90, IsEnabled = true },
                    new DataFieldConfig { Order = 10, FieldName = "ExceptionType", DisplayName = "예외 타입", ColumnWidth = 200, IsEnabled = true },
                    new DataFieldConfig { Order = 20, FieldName = "Message", DisplayName = "메시지", ColumnWidth = 400, IsEnabled = true },
                    new DataFieldConfig { Order = 30, FieldName = "Source", DisplayName = "소스", ColumnWidth = 150, IsEnabled = true },
                    new DataFieldConfig { Order = 999, FieldName = "Summary", DisplayName = "상세", ColumnWidth = 0, IsEnabled = true },
                }
            };
        }

        /// <summary>
        /// 활성화된 컬럼 필드 (Order 순서대로)
        /// </summary>
        public IEnumerable<DataFieldConfig> EnabledFields => ColumnFields
            .Where(f => f.IsEnabled)
            .OrderBy(f => f.Order);
    }

    /// <summary>
    /// DATA 로그 컬럼 필드 설정
    /// </summary>
    public class DataFieldConfig
    {
        /// <summary>
        /// 순서 (낮을수록 앞에 표시)
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// 필드명 (DataLogEntry의 프로퍼티명 또는 파라미터명)
        /// </summary>
        public string FieldName { get; set; } = "";

        /// <summary>
        /// 표시 이름
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// 컬럼 너비 (0이면 Star)
        /// </summary>
        public int ColumnWidth { get; set; } = 100;

        /// <summary>
        /// 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 파라미터 필드 여부 (XML에서 추출)
        /// </summary>
        public bool IsParameter { get; set; } = false;

        /// <summary>
        /// 값 변환 매핑 (예: "1=ON,0=OFF")
        /// </summary>
        public string ValueMapping { get; set; } = "";
    }
}
