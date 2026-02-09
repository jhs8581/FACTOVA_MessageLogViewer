using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// F/L 로그 엔트리 (PLC Core 로그)
    /// 형식: 2026-01-28 07:16:54.214 [Debug] [Module.Name] [TagName] (Type) : Value
    /// </summary>
    public class FLLogEntry : INotifyPropertyChanged
    {
        private int rowNumber;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 행 번호 (1부터 시작)
        /// </summary>
        public int RowNumber
        {
            get => rowNumber;
            set
            {
                if (rowNumber != value)
                {
                    rowNumber = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowNumber)));
                }
            }
        }

        /// <summary>
        /// 타임스탬프
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 시간 문자열 (HH:mm:ss.fff)
        /// </summary>
        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");

        /// <summary>
        /// 로그 레벨 (Debug, Info, Error 등)
        /// </summary>
        public string Level { get; set; } = "";

        /// <summary>
        /// 모듈 이름 (예: KR3__도어가조립_R도어_LOADING_NEW.KR3_도어폼_R_로딩.PLCCore)
        /// </summary>
        public string ModuleName { get; set; } = "";

        /// <summary>
        /// 태그 이름 (예: O_LB_EVENT_LOT_INFO_SEND_08)
        /// </summary>
        public string TagName { get; set; } = "";

        /// <summary>
        /// 데이터 타입 (예: Boolean, Int32, Structure 등)
        /// </summary>
        public string DataType { get; set; } = "";

        /// <summary>
        /// 값 (예: ON, OFF, 1234 등)
        /// </summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// Structure 타입의 필드들 (필드명 -> 값)
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>
        /// Structure 필드 전체 표시 (그리드 표시용)
        /// </summary>
        public string FieldsSummary
        {
            get
            {
                if (Fields.Count == 0) return Value;
                return string.Join(" | ", Fields.Select(kv => $"{kv.Key}={kv.Value}"));
            }
        }


        /// <summary>
        /// Structure 여부
        /// </summary>
        public bool IsStructure => DataType.Equals("Structure", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// CSFC 로그 여부
        /// </summary>
        public bool IsCSFC => DataType.Equals("CSFC", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 멀티라인 데이터 여부 (Structure 또는 CSFC)
        /// </summary>
        public bool HasMultilineData => IsStructure || IsCSFC;

        /// <summary>
        /// 원본 로그 라인 (멀티라인 포함)
        /// </summary>
        public string RawLine { get; set; } = "";


        /// <summary>
        /// 소스 파일명
        /// </summary>
        public string SourceFile { get; set; } = "";


        /// <summary>
        /// 시간대 (파일명에서 추출, 예: 07, 08, 09)
        /// </summary>
        public string Hour { get; set; } = "";

        /// <summary>
        /// 태그 설명 (프리셋에서 설정한 표시명)
        /// </summary>
        public string TagDescription { get; set; } = "";

        /// <summary>
        /// 태그 순번 (프리셋에서 설정한 Order)
        /// </summary>
        public int TagOrder { get; set; } = 0;

        /// <summary>
        /// 태그 그룹명 (프리셋에서 설정한 GroupName)
        /// </summary>
        public string TagGroupName { get; set; } = "";

        /// <summary>
        /// 그룹 내 시퀀스 번호 (그룹별 독립적인 순번)
        /// </summary>
        public int GroupSequence { get; set; } = 0;

        /// <summary>
        /// 기대되는 태그 순번 (순서 검증용)
        /// </summary>
        public int ExpectedTagOrder { get; set; } = 0;

        /// <summary>
        /// 태그 순서가 올바른지 여부
        /// </summary>
        public bool IsSequenceValid { get; set; } = true;

        /// <summary>
        /// 이전 엔트리의 태그 순번
        /// </summary>
        public int PreviousTagOrder { get; set; } = 0;

        /// <summary>
        /// 스텝 순번이 올바른지 여부 (1->2->3->4->1 형태)
        /// </summary>
        public bool IsStepOrderValid { get; set; } = true;

        /// <summary>
        /// 값이 ON인지 여부
        /// </summary>
        public bool IsOn => Value.Equals("ON", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 간략화된 모듈명 (마지막 부분만)
        /// </summary>
        public string ShortModuleName
        {
            get
            {
                if (string.IsNullOrEmpty(ModuleName)) return "";
                var parts = ModuleName.Split('.');
                return parts.Length > 0 ? parts[^1] : ModuleName;
            }
        }

        /// <summary>
        /// 표시용 값 (Structure/CSFC면 요약, 아니면 값)
        /// </summary>
        public string DisplayValue => HasMultilineData ? FieldsSummary : Value;

        /// <summary>
        /// 송신 여부 (I_로 시작하는 태그)
        /// </summary>
        public bool IsSend => TagName.StartsWith("I_", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 수신 여부 (O_로 시작하는 태그)
        /// </summary>
        public bool IsRecv => TagName.StartsWith("O_", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 행 배경색 (순번 오류 시 빨간색, I_=송신 연파란색, O_=수신 연녹색)
        /// </summary>
        private System.Windows.Media.Brush? _cachedBackgroundBrush = null;
        public System.Windows.Media.Brush BackgroundBrush
        {
            get
            {
                if (_cachedBackgroundBrush != null)
                    return _cachedBackgroundBrush;

                // 스텝 순번이 잘못된 경우 빨간색 하이라이트
                if (!IsStepOrderValid)
                {
                    _cachedBackgroundBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 200, 200)); // 연빨간색
                }
                else if (IsSend)
                    _cachedBackgroundBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 240, 255)); // 송신: 연파란색
                else if (IsRecv)
                    _cachedBackgroundBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 255, 230)); // 수신: 연녹색
                else
                    _cachedBackgroundBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)); // 기타: 흰색

                _cachedBackgroundBrush.Freeze();
                return _cachedBackgroundBrush;
            }
        }

        public override string ToString()
        {
            return $"[{TimeString}] [{Level}] [{TagName}] = {Value}";
        }
    }
}
