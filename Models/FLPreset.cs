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
        private string valueFilter = ""; // 값 필터 (ON, OFF 등)

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
        /// 값 필터 (빈 문자열이면 모든 값, "ON"/"OFF" 등 특정 값 지정 가능)
        /// </summary>
        public string ValueFilter
        {
            get => valueFilter;
            set { valueFilter = value; OnPropertyChanged(nameof(ValueFilter)); OnPropertyChanged(nameof(DisplayNameWithFilter)); }
        }

        /// <summary>
        /// 표시명 + 값 필터 표시 (UI용)
        /// </summary>
        [JsonIgnore]
        public string DisplayNameWithFilter
        {
            get
            {
                if (string.IsNullOrEmpty(ValueFilter))
                    return DisplayName;
                return $"{DisplayName} ({ValueFilter})";
            }
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
    /// F/L 탭의 태그 아이템 (태그명 + 순번 + 값 필터 + 그룹)
    /// </summary>
    public class FLTagItem : INotifyPropertyChanged
    {
        private string tagName = "";
        private int order = 0;
        private string valueFilter = "";
        private string groupName = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 태그명
        /// </summary>
        public string TagName
        {
            get => tagName;
            set { tagName = value; OnPropertyChanged(nameof(TagName)); OnPropertyChanged(nameof(DisplayText)); }
        }

        /// <summary>
        /// 순번 (0 = 순번 없음)
        /// </summary>
        public int Order
        {
            get => order;
            set { order = value; OnPropertyChanged(nameof(Order)); OnPropertyChanged(nameof(DisplayText)); }
        }

        /// <summary>
        /// 값 필터 (빈 문자열 = 모든 값, "ON"/"OFF" 등 특정 값)
        /// </summary>
        public string ValueFilter
        {
            get => valueFilter;
            set { valueFilter = value; OnPropertyChanged(nameof(ValueFilter)); OnPropertyChanged(nameof(DisplayText)); }
        }

        /// <summary>
        /// 그룹명 (빈 문자열 = 기본 그룹)
        /// </summary>
        public string GroupName
        {
            get => groupName;
            set { groupName = value; OnPropertyChanged(nameof(GroupName)); OnPropertyChanged(nameof(DisplayText)); }
        }

        /// <summary>
        /// 표시 텍스트 (UI용)
        /// 예: "[그룹A #1 ON] I_LB_EVENT_LOT_INFO_REQUEST_01"
        /// </summary>
        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                var parts = new List<string>();
                
                if (!string.IsNullOrEmpty(GroupName))
                    parts.Add(GroupName);
                
                if (Order > 0)
                {
                    if (!string.IsNullOrEmpty(ValueFilter))
                        parts.Add($"#{Order} {ValueFilter}");
                    else
                        parts.Add($"#{Order}");
                }
                else if (!string.IsNullOrEmpty(ValueFilter))
                {
                    parts.Add(ValueFilter);
                }

                if (parts.Count > 0)
                    return $"[{string.Join(" ", parts)}] {TagName}";
                
                return TagName;
            }
        }

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
            set 
            { 
                showAsColumn = value; 
                OnPropertyChanged(nameof(ShowAsColumn)); 
                OnPropertyChanged(nameof(DisplayTypeString)); 
            }
        }

        /// <summary>
        /// 표시 타입 문자열 (Column/Hidden) - UI 바인딩용
        /// </summary>
        [JsonIgnore]
        public string DisplayTypeString
        {
            get => ShowAsColumn ? "Column" : "Hidden";
            set
            {
                ShowAsColumn = value == "Column";
                OnPropertyChanged(nameof(DisplayTypeString));
            }
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
        private List<FLTagGroup> tagGroups = new();

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
        /// 태그 그룹 목록 (각 그룹은 독립적인 시퀀스)
        /// </summary>
        public List<FLTagGroup> TagGroups
        {
            get => tagGroups;
            set { tagGroups = value; OnPropertyChanged(nameof(TagGroups)); OnPropertyChanged(nameof(ConditionSummary)); }
        }

        /// <summary>
        /// 태그 아이템 목록 (레거시 호환용 - TagGroups로 자동 변환)
        /// </summary>
        [JsonIgnore]
        public List<FLTagItem> TagItems
        {
            get
            {
                // 모든 그룹의 태그를 하나의 리스트로 반환
                var items = new List<FLTagItem>();
                foreach (var group in TagGroups)
                {
                    items.AddRange(group.Tags);
                }
                return items;
            }
            set
            {
                // 레거시 데이터를 기본 그룹으로 변환
                if (TagGroups.Count == 0)
                {
                    TagGroups.Add(new FLTagGroup { GroupName = "기본" });
                }
                TagGroups[0].Tags = value ?? new List<FLTagItem>();
                OnPropertyChanged(nameof(ConditionSummary));
            }
        }

        /// <summary>
        /// 선택된 태그명 목록 (하위 호환용)
        /// </summary>
        [JsonIgnore]
        public List<string> SelectedTagNames
        {
            get => TagItems.Select(t => t.TagName).ToList();
            set
            {
                // 기존 string 목록을 TagItem으로 변환하여 기본 그룹에 추가
                if (TagGroups.Count == 0)
                {
                    TagGroups.Add(new FLTagGroup { GroupName = "기본" });
                }
                TagGroups[0].Tags.Clear();
                foreach (var tagName in value ?? new List<string>())
                {
                    TagGroups[0].Tags.Add(new FLTagItem { TagName = tagName });
                }
                OnPropertyChanged(nameof(ConditionSummary));
            }
        }

        /// <summary>
        /// 조건 요약
        /// </summary>
        [JsonIgnore]
        public string ConditionSummary
        {
            get
            {
                if (IsIntegrated) return "모든 로그";
                if (TagItems.Count == 0) return "조건 없음";
                
                // 그룹별로 요약
                var groups = TagItems.GroupBy(t => string.IsNullOrEmpty(t.GroupName) ? "기본" : t.GroupName).ToList();
                if (groups.Count == 1)
                {
                    var display = string.Join(", ", TagItems.Take(3).Select(t => t.DisplayText));
                    if (TagItems.Count > 3) display += $" 외 {TagItems.Count - 3}개";
                    return display;
                }
                else
                {
                    return $"{groups.Count}개 그룹, {TagItems.Count}개 태그";
                }
            }
        }

        /// <summary>
        /// 그룹명 목록 반환
        /// </summary>
        [JsonIgnore]
        public List<string> GroupNames
        {
            get
            {
                return TagItems
                    .Select(t => string.IsNullOrEmpty(t.GroupName) ? "기본" : t.GroupName)
                    .Distinct()
                    .OrderBy(g => g == "기본" ? "" : g) // 기본 그룹이 먼저
                    .ToList();
            }
        }

        /// <summary>
        /// 특정 그룹의 태그 아이템 반환
        /// </summary>
        public List<FLTagItem> GetGroupItems(string groupName)
        {
            var targetGroup = string.IsNullOrEmpty(groupName) ? "" : groupName;
            return TagItems.Where(t => 
                (string.IsNullOrEmpty(t.GroupName) && string.IsNullOrEmpty(targetGroup)) ||
                t.GroupName == targetGroup
            ).ToList();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L 태그 그룹 (독립적인 시퀀스)
    /// </summary>
    public class FLTagGroup : INotifyPropertyChanged
    {
        private string groupName = "";
        private List<FLTagItem> tags = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 그룹 이름
        /// </summary>
        public string GroupName
        {
            get => groupName;
            set { groupName = value; OnPropertyChanged(nameof(GroupName)); }
        }

        /// <summary>
        /// 그룹 내 태그 목록
        /// </summary>
        public List<FLTagItem> Tags
        {
            get => tags;
            set { tags = value; OnPropertyChanged(nameof(Tags)); }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// F/L 조건 그룹 (그룹 내 AND) - 레거시
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
        /// 모든 탭의 TagItems를 모아서 순번, 그룹명, 값 필터 매칭 (태그명 + 값)
        /// </summary>
        public static (int Order, string GroupName) GetTagOrderAndGroup(string tagName, string value)
        {
            var settings = CurrentSettings;
            var allTagItems = settings.TabSettings?.Tabs
                .SelectMany(tab => tab.TagItems)
                .Where(item => item.Order > 0)
                .ToList() ?? new List<FLTagItem>();

            // 1순위: 태그명 + 값 필터 모두 일치
            var exactMatch = allTagItems.FirstOrDefault(item =>
                item.TagName == tagName &&
                !string.IsNullOrEmpty(item.ValueFilter) &&
                item.ValueFilter.Equals(value, StringComparison.OrdinalIgnoreCase));

            if (exactMatch != null)
                return (exactMatch.Order, exactMatch.GroupName ?? "");

            // 2순위: 태그명만 일치하고 값 필터가 없는 것
            var tagOnlyMatch = allTagItems.FirstOrDefault(item =>
                item.TagName == tagName &&
                string.IsNullOrEmpty(item.ValueFilter));

            return tagOnlyMatch != null ? (tagOnlyMatch.Order, tagOnlyMatch.GroupName ?? "") : (0, "");
        }

        /// <summary>
        /// 모든 탭의 TagItems를 모아서 순번과 값 필터 매칭 (태그명 + 값) - 레거시 호환용
        /// </summary>
        public static int GetTagOrder(string tagName, string value)
        {
            var (order, _) = GetTagOrderAndGroup(tagName, value);
            return order;
        }

        /// <summary>
        /// 태그 설명 가져오기 (TagConfig에서)
        /// </summary>
        public static string GetTagDescription(string tagName)
        {
            var settings = CurrentSettings;
            var config = settings.TagConfigs.FirstOrDefault(t => 
                t.IsEnabled && 
                t.TagName == tagName && 
                !string.IsNullOrEmpty(t.DisplayName));
            
            return config?.DisplayName ?? "";
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
