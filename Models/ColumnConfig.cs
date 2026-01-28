using System;
using System.Collections.Generic;
using System.Text.Json;
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
        public List<FieldConfig> Fields { get; set; } = new();

        /// <summary>
        /// 로그 뷰어 폰트 크기
        /// </summary>
        public int FontSize { get; set; } = 11;

        /// <summary>
        /// 탭 설정 (업무별 탭 필터링)
        /// </summary>
        public TabSettings TabSettings { get; set; } = TabSettings.CreateDefault();

        /// <summary>
        /// 컬럼으로 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> ColumnFields => 
            Fields.Where(f => f.DisplayType == FieldDisplayType.Column)
                  .OrderBy(f => f.Order);

        /// <summary>
        /// Summary에 표시할 필드들
        /// </summary>
        public IEnumerable<FieldConfig> SummaryFields =>
            Fields.Where(f => f.DisplayType == FieldDisplayType.Summary)
                  .OrderBy(f => f.Order);
    }




    /// <summary>
    /// 컬럼 설정 관리자 (AppSettingsManager 래퍼)
    /// </summary>
    public static class ColumnSettingsManager
    {
        public static ColumnSettings CurrentSettings
        {
            get => AppSettingsManager.Settings.ColumnSettings ?? AppSettingsManager.CreateDefaultColumnSettings();
            set
            {
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
        /// 프리셋 로드
        /// </summary>
        public static ColumnSettings? LoadPreset(string name)
        {
            return AppSettingsManager.LoadPreset(name);
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
