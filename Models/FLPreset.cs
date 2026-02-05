using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// F/L 로그 프리셋 설정
    /// </summary>
    public class FLPreset
    {
        /// <summary>
        /// 프리셋 이름
        /// </summary>
        public string Name { get; set; } = "Default";

        /// <summary>
        /// 태그 설정 목록 (태그명 → 표시명)
        /// </summary>
        public List<FLTagConfig> TagConfigs { get; set; } = new();

        /// <summary>
        /// 탭 설정
        /// </summary>
        public FLTabSettings TabSettings { get; set; } = new();

        /// <summary>
        /// 기본 프리셋 생성
        /// </summary>
        public static FLPreset CreateDefault()
        {
            return new FLPreset
            {
                Name = "Default",
                TagConfigs = new List<FLTagConfig>(),
                TabSettings = new FLTabSettings
                {
                    Tabs = new List<FLTabConfig>
                    {
                        new FLTabConfig
                        {
                            Name = "📊 전체 로그",
                            IsIntegrated = true,
                            IsEnabled = true
                        }
                    }
                }
            };
        }
    }


    /// <summary>
    /// F/L 태그 설정 (태그명 → 표시명 매핑)
    /// </summary>
    public class FLTagConfig : INotifyPropertyChanged
    {
        private bool isSelected;
        private int order;
        private string tagName = "";
        private string displayName = "";
        private bool isEnabled = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 선택 여부 (일괄 변경용)
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        /// <summary>
        /// 순서
        /// </summary>
        public int Order
        {
            get => order;
            set { order = value; OnPropertyChanged(nameof(Order)); }
        }

        /// <summary>
        /// 태그명 (원본)
        /// </summary>
        public string TagName
        {
            get => tagName;
            set { tagName = value; OnPropertyChanged(nameof(TagName)); }
        }

        /// <summary>
        /// 표시명 (사용자 지정)
        /// </summary>
        public string DisplayName
        {
            get => displayName;
            set { displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        /// <summary>
        /// 활성화 여부
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set { isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// 샘플 값 (UI 표시용)
        /// </summary>
        [JsonIgnore]
        public string SampleValue { get; set; } = "";

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L Structure 필드 설정 (필드명 → 컬럼 설정)
    /// </summary>
    public class FLFieldConfig : INotifyPropertyChanged
    {
        private bool isSelected;
        private int order;
        private string fieldName = "";
        private string displayName = "";
        private bool showAsColumn = false;
        private string valueMapping = "";
        private int columnWidth = 80;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 선택 여부 (일괄 변경용)
        /// </summary>
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        /// <summary>
        /// 순서
        /// </summary>
        public int Order
        {
            get => order;
            set { order = value; OnPropertyChanged(nameof(Order)); }
        }

        /// <summary>
        /// 필드명 (원본)
        /// </summary>
        public string FieldName
        {
            get => fieldName;
            set { fieldName = value; OnPropertyChanged(nameof(FieldName)); }
        }

        /// <summary>
        /// 표시명 (컬럼 헤더)
        /// </summary>
        public string DisplayName
        {
            get => displayName;
            set { displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        /// <summary>
        /// 컬럼으로 표시 여부
        /// </summary>
        public bool ShowAsColumn
        {
            get => showAsColumn;
            set { showAsColumn = value; OnPropertyChanged(nameof(ShowAsColumn)); }
        }

        /// <summary>
        /// 값 매핑 (예: "1:장입,2:미장입,True:ON,False:OFF")
        /// </summary>
        public string ValueMapping
        {
            get => valueMapping;
            set { valueMapping = value; OnPropertyChanged(nameof(ValueMapping)); OnPropertyChanged(nameof(ValueMappingDisplayText)); }
        }

        /// <summary>
        /// 컬럼 너비
        /// </summary>
        public int ColumnWidth
        {
            get => columnWidth;
            set { columnWidth = value; OnPropertyChanged(nameof(ColumnWidth)); }
        }

        /// <summary>
        /// 샘플 값 (UI 표시용)
        /// </summary>
        [JsonIgnore]
        public string SampleValue { get; set; } = "";

        /// <summary>
        /// 값 매핑 버튼 텍스트
        /// </summary>
        [JsonIgnore]
        public string ValueMappingDisplayText
        {
            get
            {
                if (string.IsNullOrEmpty(ValueMapping)) return "설정 없음";
                var count = ValueMapping.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                return $"{count}개 매핑";
            }
        }

        /// <summary>
        /// 값을 디스플레이 명칭으로 변환
        /// </summary>
        public string GetDisplayValue(string value)
        {
            if (string.IsNullOrEmpty(ValueMapping) || string.IsNullOrEmpty(value))
                return value;

            var mappings = ValueMapping.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var mapping in mappings)
            {
                var parts = mapping.Split(new[] { ':' }, 2);
                if (parts.Length == 2 && parts[0].Trim().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return parts[1].Trim();
                }
            }

            return value;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L 탭 설정
    /// </summary>
    public class FLTabSettings
    {
        /// <summary>
        /// 탭 목록
        /// </summary>
        public List<FLTabConfig> Tabs { get; set; } = new();

        /// <summary>
        /// 마지막 선택한 탭 인덱스
        /// </summary>
        public int LastSelectedTabIndex { get; set; } = 0;

        /// <summary>
        /// 활성화된 탭만 반환
        /// </summary>
        [JsonIgnore]
        public IEnumerable<FLTabConfig> EnabledTabs => Tabs.Where(t => t.IsEnabled);
    }

    /// <summary>
    /// F/L 개별 탭 설정
    /// </summary>
    public class FLTabConfig : INotifyPropertyChanged
    {
        private string name = "";
        private bool isEnabled = true;
        private bool isIntegrated = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 탭 이름
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// 활성화 여부
        /// </summary>
        public bool IsEnabled
        {
            get => isEnabled;
            set { isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        /// <summary>
        /// 통합 탭 여부 (모든 로그 표시)
        /// </summary>
        public bool IsIntegrated
        {
            get => isIntegrated;
            set { isIntegrated = value; OnPropertyChanged(nameof(IsIntegrated)); }
        }

        /// <summary>
        /// 조건 그룹 (그룹 간 OR)
        /// </summary>
        public List<FLConditionGroup> ConditionGroups { get; set; } = new();

        /// <summary>
        /// 조건 요약
        /// </summary>
        [JsonIgnore]
        public string ConditionSummary
        {
            get
            {
                if (IsIntegrated) return "모든 로그";
                if (ConditionGroups.Count == 0) return "조건 없음";

                var summaries = ConditionGroups
                    .Where(g => g.TagNames.Count > 0)
                    .Select(g => string.Join(" AND ", g.TagNames.Take(3)) + (g.TagNames.Count > 3 ? "..." : ""));

                return string.Join(" OR ", summaries.Take(2)) + (ConditionGroups.Count > 2 ? " ..." : "");
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L 조건 그룹 (그룹 내 AND)
    /// </summary>
    public class FLConditionGroup : INotifyPropertyChanged
    {
        private string name = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 그룹 이름
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>
        /// 포함할 태그명 목록 (AND 조건)
        /// </summary>
        public List<string> TagNames { get; set; } = new();

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L 프리셋 관리자 (UnifiedPresetManager와 연동)
    /// </summary>
    public static class FLPresetManager
    {
        /// <summary>
        /// 현재 F/L 설정 가져오기 (UnifiedPreset에서)
        /// </summary>
        public static FLPresetSettings CurrentSettings
        {
            get
            {
                var unified = UnifiedPresetManager.CurrentPreset;
                return unified.FLSettings ?? FLPresetSettings.CreateDefault();
            }
            set
            {
                var unified = UnifiedPresetManager.CurrentPreset;
                unified.FLSettings = value;
            }
        }

        /// <summary>
        /// 현재 프리셋의 태그 설정 딕셔너리 (태그명 → 표시명)
        /// </summary>
        public static Dictionary<string, string> GetTagDescriptions()
        {
            var settings = CurrentSettings;
            return settings.TagConfigs
                .Where(t => t.IsEnabled && !string.IsNullOrEmpty(t.DisplayName))
                .ToDictionary(t => t.TagName, t => t.DisplayName);
        }

        /// <summary>
        /// 현재 프리셋의 필드 설정 목록 (컬럼으로 표시할 필드)
        /// </summary>
        public static List<FLFieldConfig> GetColumnFields()
        {
            var settings = CurrentSettings;
            return settings.FieldConfigs
                .Where(f => f.ShowAsColumn)
                .OrderBy(f => f.Order)
                .ToList();
        }

        /// <summary>
        /// 필드명으로 설정 가져오기
        /// </summary>
        public static FLFieldConfig? GetFieldConfig(string fieldName)
        {
            var settings = CurrentSettings;
            return settings.FieldConfigs.FirstOrDefault(f => f.FieldName == fieldName);
        }

        /// <summary>
        /// F/L 설정 저장 (UnifiedPreset에 저장)
        /// </summary>
        public static void SaveSettings(FLPresetSettings settings)
        {
            var unified = UnifiedPresetManager.CurrentPreset;
            unified.FLSettings = settings;
            UnifiedPresetManager.SavePreset(unified);
            System.Diagnostics.Debug.WriteLine($"💾 F/L 설정 저장 완료 (통합 프리셋: {unified.Name})");
        }
    }
}
