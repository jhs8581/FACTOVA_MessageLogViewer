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
        public List<FLTagConfig> TagConfigs { get; set; } = null!;

        /// <summary>
        /// Structure 필드 설정 (필드명 → 컬럼 설정)
        /// </summary>
        public List<FLFieldConfig> FieldConfigs { get; set; } = null!;

        /// <summary>
        /// 탭 설정
        /// </summary>
        public FLTabSettings TabSettings { get; set; } = null!;

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
                
                var fileName = SanitizeFileName(preset.Name) + ".json";
                var filePath = Path.Combine(PresetFolder, fileName);
                
                System.Diagnostics.Debug.WriteLine($"💾 프리셋 저장 시도:");
                System.Diagnostics.Debug.WriteLine($"   원본 이름: '{preset.Name}'");
                System.Diagnostics.Debug.WriteLine($"   파일명: '{fileName}'");
                System.Diagnostics.Debug.WriteLine($"   전체 경로: '{filePath}'");
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                    IncludeFields = false
                };
                
                var json = JsonSerializer.Serialize(preset, options);
                File.WriteAllText(filePath, json);
                
                System.Diagnostics.Debug.WriteLine($"✅ 통합 프리셋 저장 완료: {preset.Name}");
                System.Diagnostics.Debug.WriteLine($"   JSON 길이: {json.Length} bytes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 프리셋 저장 실패: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 프리셋 로드
        /// </summary>
        public static UnifiedPreset? LoadPreset(string name)
        {
            try
            {
                EnsurePresetFolder();
                System.Diagnostics.Debug.WriteLine($"📂 UnifiedPresetManager.LoadPreset('{name}')");
                System.Diagnostics.Debug.WriteLine($"   폴더: {PresetFolder}");
                
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(PresetFolder, fileName);
                System.Diagnostics.Debug.WriteLine($"   프리셋 경로: {filePath}");
                System.Diagnostics.Debug.WriteLine($"   파일 존재: {File.Exists(filePath)}");
                
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    System.Diagnostics.Debug.WriteLine($"   JSON 길이: {json.Length} bytes");
                    
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                        IncludeFields = false
                    };
                    
                    var preset = JsonSerializer.Deserialize<UnifiedPreset>(json, options);
                    
                    if (preset != null)
                    {
                        // 프리셋 이름 정리 (.unified 제거 - 하위 호환성)
                        if (preset.Name.EndsWith(".unified"))
                        {
                            preset.Name = preset.Name.Substring(0, preset.Name.Length - 8);
                            System.Diagnostics.Debug.WriteLine($"   ⚠️ 프리셋 이름에서 .unified 제거: '{preset.Name}'");
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"   ✅ 통합 프리셋 로드 성공");
                    System.Diagnostics.Debug.WriteLine($"      EventSettings: {preset?.EventSettings != null}");
                    System.Diagnostics.Debug.WriteLine($"      DataSettings: {preset?.DataSettings != null}");
                    System.Diagnostics.Debug.WriteLine($"      ExceptionSettings: {preset?.ExceptionSettings != null}");
                    System.Diagnostics.Debug.WriteLine($"      FLSettings: {preset?.FLSettings != null}");
                    
                    if (preset?.FLSettings != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"      FL TagConfigs: {preset.FLSettings.TagConfigs?.Count ?? 0}개");
                        System.Diagnostics.Debug.WriteLine($"      FL FieldConfigs: {preset.FLSettings.FieldConfigs?.Count ?? 0}개");
                        System.Diagnostics.Debug.WriteLine($"      FL Tabs: {preset.FLSettings.TabSettings?.Tabs?.Count ?? 0}개");
                    }
                    
                    return preset;
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
        /// 모든 프리셋 이름 목록
        /// </summary>
        public static List<string> GetPresetNames()
        {
            var names = new HashSet<string>();
            
            try
            {
                EnsurePresetFolder();
                
                System.Diagnostics.Debug.WriteLine($"📂 프리셋 폴더: {PresetFolder}");
                
                // 모든 *.json 프리셋 파일 (*.unified.json 포함)
                var jsonFiles = Directory.GetFiles(PresetFolder, "*.json");
                System.Diagnostics.Debug.WriteLine($"   프리셋: {jsonFiles.Length}개");
                
                foreach (var file in jsonFiles)
                {
                    var fullFileName = Path.GetFileName(file);
                    var name = Path.GetFileNameWithoutExtension(file);
                    
                    // .unified.json 파일에서 .unified 제거 (하위 호환성)
                    if (name.EndsWith(".unified"))
                    {
                        name = name.Substring(0, name.Length - 8); // ".unified" 제거
                    }
                    
                    var fileInfo = new FileInfo(file);
                    System.Diagnostics.Debug.WriteLine($"     - 파일: '{fullFileName}' → 이름: '{name}' ({fileInfo.Length} bytes)");
                    names.Add(name);
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
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(PresetFolder, fileName);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    System.Diagnostics.Debug.WriteLine($"🗑️ 프리셋 삭제: {name}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 프리셋 삭제 실패: {ex.Message}");
            }
            
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
