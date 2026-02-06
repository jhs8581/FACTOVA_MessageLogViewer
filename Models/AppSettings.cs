using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 앱 전체 설정 (통합)
    /// </summary>
    public class AppSettings
    {
        #region 메인 화면 설정

        /// <summary>
        /// 기본 로그 폴더 경로
        /// </summary>
        public string DefaultLogFolder { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FactovaMES", "SFC", "Logs");

        /// <summary>
        /// 마지막으로 사용한 로그 폴더
        /// </summary>
        public string LastUsedFolder { get; set; } = "";

        /// <summary>
        /// 자동 시작 여부
        /// </summary>
        public bool AutoStart { get; set; } = false;

        #endregion

        #region 컬럼/탭 설정

        /// <summary>
        /// 현재 활성화된 프리셋 이름
        /// </summary>
        public string CurrentPresetName { get; set; } = "Default";

        /// <summary>
        /// 컬럼 설정
        /// </summary>
        public ColumnSettings ColumnSettings { get; set; } = new();

        #endregion

        #region 뷰어 설정

        /// <summary>
        /// 폰트 크기
        /// </summary>
        public int FontSize { get; set; } = 11;

        /// <summary>
        /// 마지막 선택한 탭 인덱스
        /// </summary>
        public int LastSelectedTabIndex { get; set; } = 0;

        /// <summary>
        /// 로그 로드 모드 (0: NewOnly, 1: Recent, 2: All)
        /// </summary>
        public int LogLoadMode { get; set; } = 0;

        /// <summary>
        /// 최근 로그 로드 개수
        /// </summary>
        public int RecentLogCount { get; set; } = 1000;

        /// <summary>
        /// EVENT 로그 실시간 감지 여부
        /// </summary>
        public bool WatchEventLog { get; set; } = true;


        /// <summary>
        /// DATA 로그 실시간 감지 여부
        /// </summary>
        public bool WatchDataLog { get; set; } = true;

        /// <summary>
        /// EXCEPTION 로그 실시간 감지 여부
        /// </summary>
        public bool WatchExceptionLog { get; set; } = true;

        /// <summary>
        /// EVENT 로그 뷰어 시작 여부
        /// </summary>
        public bool StartEventViewer { get; set; } = true;

        /// <summary>
        /// DATA 로그 뷰어 시작 여부
        /// </summary>
        public bool StartDataViewer { get; set; } = true;

        /// <summary>
        /// EXCEPTION 로그 뷰어 시작 여부
        /// </summary>
        public bool StartExceptionViewer { get; set; } = true;

        /// <summary>
        /// F/L 로그 뷰어 시작 여부
        /// </summary>
        public bool StartFLViewer { get; set; } = true;

        /// <summary>
        /// F/L 로그 폰트 크기
        /// </summary>
        public int FLLogFontSize { get; set; } = 11;

        #endregion

        #region F/L 로그 설정

        /// <summary>
        /// F/L 로그 별도 폴더 사용 여부
        /// </summary>
        public bool UseSeparateFLFolder { get; set; } = false;

        /// <summary>
        /// F/L 로그 별도 폴더 경로
        /// </summary>
        public string FLLogFolder { get; set; } = "";

        /// <summary>
        /// F/L 로그 실시간 감지 여부
        /// </summary>
        public bool WatchFLLog { get; set; } = false;

        #endregion
    }

    /// <summary>
    /// 앱 설정 관리자
    /// </summary>
    public static class AppSettingsManager
    {
        private static readonly string AppFolder = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string SettingsFile = Path.Combine(AppFolder, "app_settings.json");
        private static readonly string PresetsFolder = Path.Combine(AppFolder, "Presets");
        private static readonly string LegacyConfigFile = Path.Combine(AppFolder, "config.txt");
        private static readonly string LegacyColumnSettingsFile = Path.Combine(AppFolder, "column_settings.json");

        private static AppSettings? _settings;

        public static AppSettings Settings
        {
            get => _settings ??= Load();
            set
            {
                _settings = value;
                Save(value);
            }
        }

        static AppSettingsManager()
        {
            Directory.CreateDirectory(PresetsFolder);
        }

        /// <summary>
        /// 설정 로드 (레거시 파일 마이그레이션 포함)
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }

                // 레거시 파일 마이그레이션
                return MigrateLegacySettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
                return new AppSettings();
            }
        }

        /// <summary>
        /// 설정 저장
        /// </summary>
        public static void Save(AppSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 현재 설정 저장 (편의 메서드)
        /// </summary>
        public static void SaveCurrent()
        {
            if (_settings != null)
            {
                Save(_settings);
            }
        }

        /// <summary>
        /// 레거시 설정 파일들을 마이그레이션
        /// </summary>
        private static AppSettings MigrateLegacySettings()
        {
            var settings = new AppSettings();

            // config.txt 마이그레이션
            if (File.Exists(LegacyConfigFile))
            {
                try
                {
                    var lines = File.ReadAllLines(LegacyConfigFile, Encoding.UTF8);
                    if (lines.Length > 0) settings.DefaultLogFolder = lines[0].Trim();
                    if (lines.Length > 1) settings.AutoStart = bool.TryParse(lines[1].Trim(), out bool result) && result;
                    if (lines.Length > 2) settings.LastUsedFolder = lines[2].Trim();
                }
                catch { }
            }

            // column_settings.json 마이그레이션
            if (File.Exists(LegacyColumnSettingsFile))
            {
                try
                {
                    var json = File.ReadAllText(LegacyColumnSettingsFile, Encoding.UTF8);
                    var columnSettings = JsonSerializer.Deserialize<ColumnSettings>(json);
                    if (columnSettings != null)
                    {
                        settings.ColumnSettings = columnSettings;
                        settings.CurrentPresetName = columnSettings.Name;
                        settings.FontSize = columnSettings.FontSize > 0 ? columnSettings.FontSize : 11;
                        if (columnSettings.TabSettings != null)
                        {
                            settings.LastSelectedTabIndex = columnSettings.TabSettings.LastSelectedTabIndex;
                        }
                    }
                }
                catch { }
            }

            // 마이그레이션 후 새 형식으로 저장
            Save(settings);

            return settings;
        }

        #region 프리셋 관리

        /// <summary>
        /// 프리셋 저장
        /// </summary>
        public static void SavePreset(string name, ColumnSettings columnSettings)
        {
            try
            {
                columnSettings.Name = name;
                columnSettings.ModifiedAt = DateTime.Now;
                
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(PresetsFolder, fileName);
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(columnSettings, options);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"프리셋 저장 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 프리셋 로드
        /// </summary>
        public static ColumnSettings? LoadPreset(string name)
        {
            try
            {
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(PresetsFolder, fileName);
                
                // 새 경로에 없으면 레거시 경로 확인
                if (!File.Exists(filePath))
                {
                    var legacyPath = Path.Combine(AppFolder, "ColumnSettings", fileName);
                    if (File.Exists(legacyPath))
                    {
                        filePath = legacyPath;
                    }
                }

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath, Encoding.UTF8);
                    var settings = JsonSerializer.Deserialize<ColumnSettings>(json);
                    
                    if (settings != null && settings.TabSettings == null)
                    {
                        settings.TabSettings = TabSettings.CreateDefault();
                    }
                    
                    return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"프리셋 로드 실패: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// 저장된 프리셋 목록 조회 (기존 EVENT 프리셋만, unified 제외)
        /// </summary>
        public static List<string> GetPresetNames()
        {
            var presets = new List<string>();
            try
            {
                // 새 경로 (unified 제외)
                if (Directory.Exists(PresetsFolder))
                {
                    foreach (var file in Directory.GetFiles(PresetsFolder, "*.json"))
                    {
                        // .unified.json 파일은 제외
                        if (file.EndsWith(".unified.json", StringComparison.OrdinalIgnoreCase))
                            continue;
                            
                        presets.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }

                // 레거시 경로
                var legacyFolder = Path.Combine(AppFolder, "ColumnSettings");
                if (Directory.Exists(legacyFolder))
                {
                    foreach (var file in Directory.GetFiles(legacyFolder, "*.json"))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (!presets.Contains(name))
                        {
                            presets.Add(name);
                        }
                    }
                }
            }
            catch { }
            return presets;
        }

        /// <summary>
        /// 프리셋 삭제
        /// </summary>
        public static bool DeletePreset(string name)
        {
            try
            {
                var fileName = SanitizeFileName(name) + ".json";
                var filePath = Path.Combine(PresetsFolder, fileName);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
            }
            catch { }
            
            return false;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        #endregion

        #region 기본 설정 생성

        /// <summary>
        /// 기본 컬럼 설정 생성
        /// </summary>
        public static ColumnSettings CreateDefaultColumnSettings()
        {
            return new ColumnSettings
            {
                Name = "Default",
                Fields = new List<FieldConfig>
                {
                    new() { FieldName = "RETURN_CODE", DisplayName = "Result", DisplayType = FieldDisplayType.Column, ColumnWidth = 60, Order = 1 },
                    new() { FieldName = "WORK_TYPE", DisplayName = "Work", DisplayType = FieldDisplayType.Column, ColumnWidth = 50, Order = 2 },
                    new() { FieldName = "ERROR_CODE", DisplayName = "Error", DisplayType = FieldDisplayType.Column, ColumnWidth = 80, Order = 3 },
                    new() { FieldName = "LOTID", DisplayName = "LOT", DisplayType = FieldDisplayType.Summary, Order = 10 },
                    new() { FieldName = "PALLET_ID", DisplayName = "Pallet", DisplayType = FieldDisplayType.Summary, Order = 11 },
                    new() { FieldName = "PROCID", DisplayName = "ProcID", DisplayType = FieldDisplayType.Hidden, Order = 100 }
                },
                TabSettings = TabSettings.CreateDefault(),
                FontSize = 11
            };
        }

        #endregion
    }
}
