using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 통합 프리셋 - EVENT/DATA/EXCEPTION/F/L 탭 설정을 하나로 관리
    /// </summary>
    public class UnifiedPreset
    {
        /// <summary>
        /// 프리셋 이름
        /// </summary>
        public string Name { get; set; } = "Default";

        /// <summary>
        /// EVENT 로그 설정
        /// </summary>
        public ColumnSettings? EventSettings { get; set; }

        /// <summary>
        /// DATA 로그 설정
        /// </summary>
        public DataColumnSettings? DataSettings { get; set; }

        /// <summary>
        /// EXCEPTION 로그 설정
        /// </summary>
        public DataColumnSettings? ExceptionSettings { get; set; }

        /// <summary>
        /// F/L 로그 설정
        /// </summary>
        public FLPresetSettings? FLSettings { get; set; }

        /// <summary>
        /// 생성 일시
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 수정 일시
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 기본 프리셋 생성
        /// </summary>
        public static UnifiedPreset CreateDefault()
        {
            return new UnifiedPreset
            {
                Name = "Default",
                EventSettings = ColumnSettingsManager.CreateDefaultSettings(),
                DataSettings = DataColumnSettings.CreateDefault(),
                ExceptionSettings = DataColumnSettings.CreateExceptionDefault(),
                FLSettings = FLPresetSettings.CreateDefault()
            };
        }
    }

    /// <summary>
    /// F/L 프리셋 설정 (UnifiedPreset 내부용)
    /// </summary>
    public class FLPresetSettings
    {
        /// <summary>
        /// 태그 설정 목록 (태그명 → 표시명)
        /// </summary>
        public List<FLTagConfig> TagConfigs { get; set; } = new();

        /// <summary>
        /// Structure 필드 설정 (필드명 → 컬럼 설정)
        /// </summary>
        public List<FLFieldConfig> FieldConfigs { get; set; } = new();

        /// <summary>
        /// 탭 설정
        /// </summary>
        public FLTabSettings TabSettings { get; set; } = new();

        /// <summary>
        /// 기본 설정 생성
        /// </summary>
        public static FLPresetSettings CreateDefault()
        {
            return new FLPresetSettings
            {
                TagConfigs = new List<FLTagConfig>(),
                FieldConfigs = new List<FLFieldConfig>(),
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
    /// 통합 프리셋 관리자
    /// </summary>
    public static class UnifiedPresetManager
    {
        /// <summary>
        /// 프리셋 폴더 (실행 파일과 동일한 경로의 Presets 폴더)
        /// </summary>
        private static readonly string PresetFolder = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Presets");

        private static UnifiedPreset _currentPreset = UnifiedPreset.CreateDefault();

        /// <summary>
        /// 현재 활성 프리셋
        /// </summary>
        public static UnifiedPreset CurrentPreset
        {
            get => _currentPreset;
            set
            {
                _currentPreset = value ?? UnifiedPreset.CreateDefault();
                
                // EVENT 설정도 동기화
                if (_currentPreset.EventSettings != null)
                {
                    ColumnSettingsManager.CurrentSettings = _currentPreset.EventSettings;
                }
            }
        }

        /// <summary>
        /// 프리셋 폴더 확인/생성
        /// </summary>
        private static void EnsurePresetFolder()
        {
            if (!Directory.Exists(PresetFolder))
                Directory.CreateDirectory(PresetFolder);
        }

        /// <summary>
        /// 프리셋 저장
        /// </summary>
        public static void SavePreset(UnifiedPreset preset)
        {
            try
            {
                EnsurePresetFolder();
                preset.ModifiedAt = DateTime.Now;
                
                var fileName = SanitizeFileName(preset.Name) + ".unified.json";
                var filePath = Path.Combine(PresetFolder, fileName);
                
                var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
                
                
                System.Diagnostics.Debug.WriteLine($"💾 통합 프리셋 저장: {preset.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"프리셋 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 프리셋 로드 (통합 프리셋 우선, 없으면 기존 EVENT 프리셋 자동 변환)
        /// </summary>
        public static UnifiedPreset? LoadPreset(string name)
        {
            try
            {
                EnsurePresetFolder();
                System.Diagnostics.Debug.WriteLine($"📂 UnifiedPresetManager.LoadPreset('{name}')");
                System.Diagnostics.Debug.WriteLine($"   폴더: {PresetFolder}");
                
                // 1. 먼저 통합 프리셋 찾기
                var unifiedFileName = SanitizeFileName(name) + ".unified.json";
                var unifiedFilePath = Path.Combine(PresetFolder, unifiedFileName);
                System.Diagnostics.Debug.WriteLine($"   통합 프리셋 경로: {unifiedFilePath}");
                System.Diagnostics.Debug.WriteLine($"   파일 존재: {File.Exists(unifiedFilePath)}");
                
                if (File.Exists(unifiedFilePath))
                {
                    var json = File.ReadAllText(unifiedFilePath);
                    var preset = JsonSerializer.Deserialize<UnifiedPreset>(json);
                    System.Diagnostics.Debug.WriteLine($"   ✅ 통합 프리셋 로드 성공");
                    System.Diagnostics.Debug.WriteLine($"      EventSettings: {preset?.EventSettings != null}");
                    System.Diagnostics.Debug.WriteLine($"      Fields: {preset?.EventSettings?.Fields?.Count ?? 0}개");
                    return preset;
                }
                
                // 2. 통합 프리셋 없으면 기존 EVENT 프리셋 찾아서 변환
                var legacyFileName = SanitizeFileName(name) + ".json";
                var legacyFilePath = Path.Combine(PresetFolder, legacyFileName);
                System.Diagnostics.Debug.WriteLine($"   기존 프리셋 경로: {legacyFilePath}");
                System.Diagnostics.Debug.WriteLine($"   파일 존재: {File.Exists(legacyFilePath)}");
                
                if (File.Exists(legacyFilePath))
                {
                    var json = File.ReadAllText(legacyFilePath);
                    var eventSettings = JsonSerializer.Deserialize<ColumnSettings>(json);
                    
                    if (eventSettings != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔄 기존 프리셋 변환 성공");
                        System.Diagnostics.Debug.WriteLine($"      Fields: {eventSettings.Fields?.Count ?? 0}개");
                        
                        return new UnifiedPreset
                        {
                            Name = name,
                            EventSettings = eventSettings,
                            DataSettings = null,
                            CreatedAt = eventSettings.CreatedAt,
                            ModifiedAt = eventSettings.ModifiedAt
                        };
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"   ❌ 프리셋을 찾을 수 없음");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 프리셋 로드 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 모든 프리셋 이름 목록 (통합 프리셋 + 기존 EVENT 프리셋)
        /// </summary>
        public static List<string> GetPresetNames()
        {
            var names = new HashSet<string>();
            
            try
            {
                EnsurePresetFolder();
                
                System.Diagnostics.Debug.WriteLine($"📂 프리셋 폴더: {PresetFolder}");
                
                // 통합 프리셋 (*.unified.json)
                var unifiedFiles = Directory.GetFiles(PresetFolder, "*.unified.json");
                System.Diagnostics.Debug.WriteLine($"   통합 프리셋: {unifiedFiles.Length}개");
                foreach (var file in unifiedFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file).Replace(".unified", "");
                    System.Diagnostics.Debug.WriteLine($"     - {name} (unified)");
                    names.Add(name);
                }
                
                // 기존 EVENT 프리셋 (*.json, unified 제외)
                var legacyFiles = Directory.GetFiles(PresetFolder, "*.json")
                    .Where(f => !f.EndsWith(".unified.json")).ToArray();
                System.Diagnostics.Debug.WriteLine($"   기존 프리셋: {legacyFiles.Length}개");
                foreach (var file in legacyFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(name))
                    {
                        System.Diagnostics.Debug.WriteLine($"     - {name} (legacy)");
                        names.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 프리셋 목록 로드 실패: {ex.Message}");
            }
            
            System.Diagnostics.Debug.WriteLine($"📂 총 {names.Count}개 프리셋");
            return names.OrderBy(n => n).ToList();
        }


        /// <summary>
        /// 프리셋 삭제
        /// </summary>
        public static bool DeletePreset(string name)
        {
            try
            {
                var fileName = SanitizeFileName(name) + ".unified.json";
                var filePath = Path.Combine(PresetFolder, fileName);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }
            
            return false;
        }

        /// <summary>
        /// 프리셋 폴더 경로 반환
        /// </summary>
        public static string GetPresetFolderPath() => PresetFolder;

        /// <summary>
        /// 현재 프리셋 저장
        /// </summary>
        public static void SaveCurrentPreset()
        {
            SavePreset(CurrentPreset);
        }

        /// <summary>
        /// 파일명으로 사용 가능한 문자로 변환
        /// </summary>
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
