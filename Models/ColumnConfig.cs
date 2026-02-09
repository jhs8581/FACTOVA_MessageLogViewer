using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 필드 표시 타입
    /// </summary>
    public enum FieldDisplayType
    {
        Column,     // 개별 컬럼으로 표시
        Summary,    // Summary 컬럼에 함께 표시
        Hidden      // 숨김
    }

    /// <summary>
    /// 개별 필드 설정
    /// </summary>
    public class FieldConfig
    {
        public string FieldName { get; set; } = "";
        public string DisplayName { get; set; } = "";  // 컬럼 헤더에 표시할 이름
        public FieldDisplayType DisplayType { get; set; } = FieldDisplayType.Summary;
        public int ColumnWidth { get; set; } = 100;
        public int Order { get; set; } = 0;  // 컬럼 순서
        public bool IsSelected { get; set; } = false;  // 일괄 변경용 체크박스
        
        /// <summary>
        /// 값 매핑 (예: "1:장입,2:미장입")
        /// </summary>
        public string ValueMapping { get; set; } = "";
        
        /// <summary>
        /// 이 컬럼을 표시할 탭 이름 목록 (null이면 모든 탭에 표시)
        /// </summary>
        public List<string>? VisibleInTabs { get; set; } = null;
        
        /// <summary>
        /// UI 바인딩용 (쉼표로 구분된 문자열) - JSON에서 제외
        /// </summary>
        [JsonIgnore]
        public string VisibleTabsString
        {
            get
            {
                if (VisibleInTabs == null || VisibleInTabs.Count == 0)
                    return "";
                return string.Join(",", VisibleInTabs);
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    VisibleInTabs = null;
                }
                else
                {
                    VisibleInTabs = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(s => s.Trim())
                                         .ToList();
                }
            }
        }
        
        /// <summary>
        /// UI 표시용 (버튼에 표시될 텍스트) - JSON에서 제외
        /// </summary>
        [JsonIgnore]
        public string VisibleTabsDisplayText
        {
            get
            {
                if (VisibleInTabs == null || VisibleInTabs.Count == 0)
                    return "전체 탭";
                if (VisibleInTabs.Count > 2)
                    return $"{VisibleInTabs[0]} 외 {VisibleInTabs.Count - 1}개";
                return string.Join(", ", VisibleInTabs);
            }
        }
        
        /// <summary>
        /// 특정 탭에서 이 컬럼이 표시되어야 하는지 확인
        /// </summary>
        public bool IsVisibleInTab(string tabName)
        {
            // null이면 모든 탭에 표시
            if (VisibleInTabs == null || VisibleInTabs.Count == 0)
                return true;
            
            return VisibleInTabs.Contains(tabName);
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
    }

    /// <summary>
    /// 전체 컬럼 설정 (프리셋으로 저장)
    /// </summary>
    public class ColumnSettings
    {
        public string Name { get; set; } = "기본 설정";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
        
        /// <summary>
        /// EVENT 로그 필드 설정
        /// </summary>
        public List<FieldConfig> Fields { get; set; } = new();

        /// <summary>
        /// DATA 로그 필드 설정
        /// </summary>
        public List<FieldConfig> DataFields { get; set; } = new();

        /// <summary>
        /// 제외할 MSGID 목록 (쉼표로 구분, 예: "S6F1,S6F11,S1F3")
        /// </summary>
        public string ExcludedMsgIds { get; set; } = "";

        /// <summary>
        /// 제외할 MSGID 목록 (파싱된)
        /// </summary>
        [JsonIgnore]
        public HashSet<string> ExcludedMsgIdSet
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ExcludedMsgIds))
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                return new HashSet<string>(
                    ExcludedMsgIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Trim()),
                    StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 포함할 키워드 목록 (줄바꿈으로 구분)
        /// MSGID가 없는 로그도 해당 키워드가 포함되면 수집
        /// </summary>
        public string IncludeKeywords { get; set; } = "";

        /// <summary>
        /// 포함할 키워드 목록 (파싱된)
        /// </summary>
        [JsonIgnore]
        public List<string> IncludeKeywordList
        {
            get
            {
                if (string.IsNullOrWhiteSpace(IncludeKeywords))
                    return new List<string>();
                    
                return IncludeKeywords
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        /// <summary>
        /// 로그 뷰어 폰트 크기
        /// </summary>
        public int FontSize { get; set; } = 11;

        /// <summary>
        /// 탭 설정 (업무별 탭 필터링) - EVENT 로그용
        /// </summary>
        public TabSettings TabSettings { get; set; } = TabSettings.CreateDefault();

        /// <summary>
        /// DATA 로그 탭 설정
        /// </summary>
        public TabSettings DataTabSettings { get; set; } = TabSettings.CreateDataDefault();

        /// <summary>
        /// EVENT 로그 컬럼으로 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> ColumnFields => 
            Fields.Where(f => f.DisplayType == FieldDisplayType.Column)
                  .OrderBy(f => f.Order);

        /// <summary>
        /// EVENT 로그 Summary에 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> SummaryFields =>
            Fields.Where(f => f.DisplayType == FieldDisplayType.Summary)
                  .OrderBy(f => f.Order);

        /// <summary>
        /// DATA 로그 컬럼으로 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> DataColumnFields => 
            DataFields.Where(f => f.DisplayType == FieldDisplayType.Column)
                      .OrderBy(f => f.Order);

        /// <summary>
        /// DATA 로그 Summary에 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> DataSummaryFields =>
            DataFields.Where(f => f.DisplayType == FieldDisplayType.Summary)
                      .OrderBy(f => f.Order);
    }




    /// <summary>
    /// 컬럼 설정 관리자 (UnifiedPresetManager 연동)
    /// </summary>
    public static class ColumnSettingsManager
    {
        public static ColumnSettings CurrentSettings
        {
            get
            {
                // UnifiedPresetManager에서 현재 프리셋의 EVENT 설정 가져오기
                var unified = UnifiedPresetManager.CurrentPreset;
                return unified.EventSettings ?? AppSettingsManager.CreateDefaultColumnSettings();
            }
            set
            {
                // UnifiedPresetManager의 현재 프리셋에 설정 저장
                var unified = UnifiedPresetManager.CurrentPreset;
                unified.EventSettings = value;
                
                // AppSettings에도 동기화 (하위 호환성)
                AppSettingsManager.Settings.ColumnSettings = value;
                AppSettingsManager.Settings.CurrentPresetName = value.Name;
                AppSettingsManager.SaveCurrent();
            }
        }

        /// <summary>
        /// 기본 설정 생성
        /// </summary>
        public static ColumnSettings CreateDefaultSettings()
        {
            return AppSettingsManager.CreateDefaultColumnSettings();
        }

        /// <summary>
        /// 현재 설정 저장
        /// </summary>
        public static void SaveCurrentSettings(ColumnSettings settings)
        {
            AppSettingsManager.Settings.ColumnSettings = settings;
            AppSettingsManager.SaveCurrent();
        }

        /// <summary>
        /// 설정을 이름으로 저장 (프리셋)
        /// </summary>
        public static void SaveSettingsAsPreset(ColumnSettings settings, string name)
        {
            AppSettingsManager.SavePreset(name, settings);
        }

        /// <summary>
        /// 저장된 프리셋 목록 조회
        /// </summary>
        public static List<string> GetPresetNames()
        {
            return AppSettingsManager.GetPresetNames();
        }

        /// <summary>
        /// 프리셋 삭제
        /// </summary>
        public static bool DeletePreset(string name)
        {
            return AppSettingsManager.DeletePreset(name);
        }

        /// <summary>
        /// 프리셋 로드 (통합 프리셋 우선)
        /// </summary>
        public static ColumnSettings? LoadPreset(string name)
        {
            // 1. 통합 프리셋에서 먼저 로드 시도
            var unifiedPreset = UnifiedPresetManager.LoadPreset(name);
            if (unifiedPreset?.EventSettings != null)
            {
                System.Diagnostics.Debug.WriteLine($"📂 ColumnSettingsManager.LoadPreset: 통합 프리셋에서 로드 - {name}");
                return unifiedPreset.EventSettings;
            }
            
            // 2. 기존 프리셋 로드
            var legacySettings = AppSettingsManager.LoadPreset(name);
            if (legacySettings != null)
            {
                System.Diagnostics.Debug.WriteLine($"📂 ColumnSettingsManager.LoadPreset: 기존 프리셋에서 로드 - {name}");
            }
            
            return legacySettings;
        }

        /// <summary>
        /// 필드 설정 가져오기 (없으면 Summary로 기본 생성)
        /// </summary>
        public static FieldConfig GetFieldConfig(string fieldName)
        {
            var existing = CurrentSettings.Fields.FirstOrDefault(f => f.FieldName == fieldName);
            if (existing != null)
                return existing;

            // 새 필드 - Summary로 기본 설정
            return new FieldConfig
            {
                FieldName = fieldName,
                DisplayName = fieldName,
                DisplayType = FieldDisplayType.Summary
            };
        }
    }
}
