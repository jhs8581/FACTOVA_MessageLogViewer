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
    }

    /// <summary>
    /// 전체 컬럼 설정 (공정별로 저장)
    /// </summary>
    public class ColumnSettings
    {
        public string Name { get; set; } = "기본 설정";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime ModifiedAt { get; set; } = DateTime.Now;
        public List<FieldConfig> Fields { get; set; } = new();

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
    /// 컬럼 설정 관리자
    /// </summary>
    public static class ColumnSettingsManager
    {
        // 실행파일과 동일한 경로에 저장
        private static readonly string AppFolder = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string SettingsFolder = Path.Combine(AppFolder, "ColumnSettings");
        private static readonly string CurrentSettingFile = Path.Combine(AppFolder, "column_settings.json");

        private static ColumnSettings? _currentSettings;

        public static ColumnSettings CurrentSettings
        {
            get => _currentSettings ?? LoadCurrentSettings() ?? CreateDefaultSettings();
            set
            {
                _currentSettings = value;
                SaveCurrentSettings(value);
            }
        }


        static ColumnSettingsManager()
        {
            // 폴더 생성
            Directory.CreateDirectory(SettingsFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(CurrentSettingFile)!);
        }

        /// <summary>
        /// 기본 설정 생성
        /// </summary>
        public static ColumnSettings CreateDefaultSettings()
        {
            return new ColumnSettings
            {
                Name = "기본 설정",
                Fields = new List<FieldConfig>
                {
                    new() { FieldName = "RETURN_CODE", DisplayName = "결과", DisplayType = FieldDisplayType.Column, ColumnWidth = 60, Order = 1 },
                    new() { FieldName = "WORK_TYPE", DisplayName = "작업", DisplayType = FieldDisplayType.Column, ColumnWidth = 50, Order = 2 },
                    new() { FieldName = "ERROR_CODE", DisplayName = "에러", DisplayType = FieldDisplayType.Column, ColumnWidth = 80, Order = 3 },
                    new() { FieldName = "LOTID", DisplayName = "LOT", DisplayType = FieldDisplayType.Summary, Order = 10 },
                    new() { FieldName = "PALLET_ID", DisplayName = "팔레트", DisplayType = FieldDisplayType.Summary, Order = 11 },
                    new() { FieldName = "PROCID", DisplayName = "공정ID", DisplayType = FieldDisplayType.Hidden, Order = 100 }
                }
            };
        }

        /// <summary>
        /// 현재 설정 로드
        /// </summary>
        public static ColumnSettings? LoadCurrentSettings()
        {
            try
            {
                if (File.Exists(CurrentSettingFile))
                {
                    var json = File.ReadAllText(CurrentSettingFile);
                    return JsonSerializer.Deserialize<ColumnSettings>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 현재 설정 저장
        /// </summary>
        public static void SaveCurrentSettings(ColumnSettings settings)
        {
            try
            {
                settings.ModifiedAt = DateTime.Now;
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(CurrentSettingFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 설정을 이름으로 저장 (프리셋)
        /// </summary>
        public static void SaveSettingsAsPreset(ColumnSettings settings, string name)
        {
            try
            {
                settings.Name = name;
                settings.ModifiedAt = DateTime.Now;
                
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(SettingsFolder, fileName);
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"프리셋 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 저장된 프리셋 목록 조회
        /// </summary>
        public static List<string> GetPresetNames()
        {
            var presets = new List<string>();
            try
            {
                if (Directory.Exists(SettingsFolder))
                {
                    foreach (var file in Directory.GetFiles(SettingsFolder, "*.json"))
                    {
                        presets.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch { }
            return presets;
        }

        /// <summary>
        /// 프리셋 로드
        /// </summary>
        public static ColumnSettings? LoadPreset(string name)
        {
            try
            {
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(SettingsFolder, fileName);
                
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<ColumnSettings>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"프리셋 로드 실패: {ex.Message}");
            }
            return null;
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

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
