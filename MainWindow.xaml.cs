using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using FACTOVA_MessageLogViewer.Models;
using FACTOVA_MessageLogViewer.Presets;

namespace FACTOVA_MessageLogViewer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SetWindowTitle();
        }

        /// <summary>
        /// 윈도우 타이틀에 버전 정보 설정
        /// </summary>
        private void SetWindowTitle()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                var version = fileVersionInfo.FileVersion ?? assembly.GetName().Version?.ToString() ?? "1.0.0";
                Title = $"FACTOVA 로그 뷰어 v{version}";
            }
            catch
            {
                Title = "FACTOVA 로그 뷰어";
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 저장된 설정 로드
            LoadConfig();

            // 프리셋 목록 로드
            logSettings.LoadPresetList();
        }

        private void LoadConfig()
        {
            var settings = AppSettingsManager.Settings;

            // 마지막 사용 폴더가 있으면 해당 폴더로 시작
            if (!string.IsNullOrEmpty(settings.LastUsedFolder) && Directory.Exists(settings.LastUsedFolder))
            {
                logSettings.SetCustomFolder(settings.LastUsedFolder);
            }
            else if (!string.IsNullOrEmpty(settings.DefaultLogFolder) && Directory.Exists(settings.DefaultLogFolder))
            {
                logSettings.SetDefaultFolder(settings.DefaultLogFolder);
            }

            // 날짜 목록 갱신
            logSettings.RefreshAvailableDates();
        }

        /// <summary>
        /// 시작 버튼 클릭 시 호출
        /// </summary>
        private async void LogSettings_StartViewerRequested(object? sender, System.EventArgs e)
        {
            var selectedDate = logSettings.SelectedDate;
            if (selectedDate == null)
            {
                MessageBox.Show("날짜를 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 선택된 프리셋 적용
            ApplySelectedPreset();

            // LogViewerSettings 생성
            var viewerSettings = new LogViewerSettings
            {
                LogFilePath = selectedDate.FilePath,
                SelectedDate = selectedDate.Date,
                LoadMode = logSettings.LoadMode,
                RecentCount = logSettings.RecentCount,
                FilterStartTime = logSettings.FilterStartTime,
                FilterEndTime = logSettings.FilterEndTime,
                SlowQueryOnly = logSettings.SlowQueryOnly,
                LogDirectory = logSettings.CurrentLogDirectory,
                IsDefaultFolder = logSettings.IsDefaultFolder
            };

            // EVENT 로그 파일 경로 찾기
            var eventFilePath = FindLogFilePath(selectedDate.Date, LogType.EVENT);
            System.Diagnostics.Debug.WriteLine($"📂 EVENT 파일 검색: {eventFilePath ?? "(찾을 수 없음)"}");
            System.Diagnostics.Debug.WriteLine($"📂 EVENT 시작 옵션: {logSettings.StartEvent}");
            
            if (!string.IsNullOrEmpty(eventFilePath) && logSettings.StartEvent)
            {
                System.Diagnostics.Debug.WriteLine($"✅ EVENT 로그 초기화 시작");
                var eventSettings = viewerSettings with 
                { 
                    LogFilePath = eventFilePath, 
                    LogType = LogType.EVENT,
                    EnableRealTimeWatch = logSettings.WatchEventLog
                };
                await eventLogViewer.InitializeAsync(eventSettings);
            }
            else
            {
                if (string.IsNullOrEmpty(eventFilePath))
                    System.Diagnostics.Debug.WriteLine($"⚠️ EVENT 로그 파일을 찾을 수 없습니다");
                if (!logSettings.StartEvent)
                    System.Diagnostics.Debug.WriteLine($"⚠️ EVENT 로그 시작 옵션이 꺼져있습니다");
            }

            // DATA 로그 파일 경로 찾기
            var dataFilePath = FindLogFilePath(selectedDate.Date, LogType.DATA);
            if (!string.IsNullOrEmpty(dataFilePath) && logSettings.StartData)
            {
                var dataSettings = viewerSettings with 
                { 
                    LogFilePath = dataFilePath, 
                    LogType = LogType.DATA,
                    EnableRealTimeWatch = logSettings.WatchDataLog
                };
                dataLogViewer.Initialize(dataSettings);
            }

            // EXCEPTION 로그 파일 경로 찾기
            var exceptionFilePath = FindLogFilePath(selectedDate.Date, LogType.EXCEPTION);
            if (!string.IsNullOrEmpty(exceptionFilePath) && logSettings.StartException)
            {
                var exceptionSettings = viewerSettings with 
                { 
                    LogFilePath = exceptionFilePath, 
                    LogType = LogType.EXCEPTION,
                    EnableRealTimeWatch = logSettings.WatchExceptionLog
                };
                exceptionLogViewer.Initialize(exceptionSettings);
            }


            // F/L 로그 초기화
            // 별도 폴더 사용 여부에 따라 경로 결정
            if (logSettings.StartFL)
            {
                var flLogDirectory = logSettings.UseSeparateFLFolder && !string.IsNullOrEmpty(logSettings.FLLogFolderPath)
                    ? logSettings.FLLogFolderPath
                    : logSettings.CurrentLogDirectory;
                
                // F/L 실시간 감지는 별도 폴더 사용 시에만 가능
                var watchFL = logSettings.UseSeparateFLFolder && logSettings.WatchFLLog;
                await flLogViewer.InitializeAsync(flLogDirectory, selectedDate.Date, watchFL);
            }

            // 표시 옵션 저장
            logSettings.SaveDisplaySettings();


            // 설정 저장
            AppSettingsManager.Settings.LastUsedFolder = logSettings.CurrentLogDirectory;
            AppSettingsManager.Settings.CurrentPresetName = logSettings.SelectedPresetName;
            AppSettingsManager.SaveCurrent();
        }

        /// <summary>
        /// 선택된 프리셋 적용
        /// </summary>
        private void ApplySelectedPreset()
        {
            var presetName = logSettings.SelectedPresetName;
            
            System.Diagnostics.Debug.WriteLine($"🎨 ApplySelectedPreset 시작: {presetName}");
            
            // 모든 프리셋을 파일에서 로드 (Default 포함)
            var preset = UnifiedPresetManager.LoadPreset(presetName);
            if (preset != null)
            {
                UnifiedPresetManager.CurrentPreset = preset;
                System.Diagnostics.Debug.WriteLine($"✅ 프리셋 로드 성공: {presetName}");
                System.Diagnostics.Debug.WriteLine($"   - EventSettings: {(preset.EventSettings != null ? "있음" : "없음")}");
                System.Diagnostics.Debug.WriteLine($"   - DataSettings: {(preset.DataSettings != null ? "있음" : "없음")}");
                System.Diagnostics.Debug.WriteLine($"   - FLSettings: {(preset.FLSettings != null ? "있음" : "없음")}");
                if (preset.FLSettings != null)
                {
                    System.Diagnostics.Debug.WriteLine($"   - FLSettings 탭: {preset.FLSettings.TabSettings?.Tabs?.Count ?? 0}개");
                    System.Diagnostics.Debug.WriteLine($"   - FLSettings 태그: {preset.FLSettings.TagConfigs?.Count ?? 0}개");
                    System.Diagnostics.Debug.WriteLine($"   - FLSettings 필드: {preset.FLSettings.FieldConfigs?.Count ?? 0}개");
                }
            }
            else
            {
                // 프리셋 파일이 없으면 기본 프리셋 생성
                UnifiedPresetManager.CurrentPreset = UnifiedPreset.CreateDefault();
                System.Diagnostics.Debug.WriteLine($"⚠️ 프리셋 로드 실패, 기본 프리셋 사용: {presetName}");
            }

            System.Diagnostics.Debug.WriteLine($"🎨 프리셋 적용: {presetName}");
        }

        /// <summary>
        /// 날짜와 로그 타입으로 로그 파일 경로 찾기
        /// </summary>
        private string? FindLogFilePath(DateTime date, LogType logType)
        {
            var baseDir = logSettings.CurrentLogDirectory;
            if (string.IsNullOrEmpty(baseDir)) return null;

            var prefix = logType switch
            {
                LogType.EVENT => "LGE GMES_EVENT_",
                LogType.DATA => "LGE GMES_DATA_",
                LogType.DEBUG => "LGE GMES_DEBUG_",
                LogType.EXCEPTION => "LGE GMES_EXCEPTION_",
                _ => ""
            };

            var fileName = $"{prefix}{date:MMddyyyy}.log";

            // 기본폴더 구조: baseDir/yyyy/MM/filename
            if (logSettings.IsDefaultFolder)
            {
                var filePath = Path.Combine(baseDir, date.Year.ToString(), date.Month.ToString("D2"), fileName);
                if (File.Exists(filePath)) return filePath;
            }

            // 사용자 폴더: 직접 검색
            try
            {
                var files = Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
            catch { }

            return null;
        }

        private void LogSettings_FolderChanged(object? sender, System.EventArgs e)
        {
            // 폴더 변경 시 날짜 목록 자동 갱신됨 (LogSettingsControl 내부에서 처리)
        }


        /// <summary>
        /// 프리셋 설정 버튼 클릭
        /// </summary>
        private void LogSettings_PresetSettingsRequested(object? sender, System.EventArgs e)
        {
            // 현재 탭에 따라 다른 설정창 열기
            var currentTabIndex = mainTabControl.SelectedIndex;
            var currentPresetName = logSettings.SelectedPresetName;
            
            if (currentTabIndex == 1) // DATA 탭
            {
                // DATA 로그 프리셋 설정창 (현재 선택된 프리셋 이름 전달)
                var settingsWindow = new DataPresetEditor(currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
                    
                    // 적용된 프리셋 선택
                    var appliedPresetName = AppSettingsManager.Settings.CurrentPresetName;
                    if (!string.IsNullOrEmpty(appliedPresetName))
                    {
                        logSettings.SelectPreset(appliedPresetName);
                    }
                }
            }
            else if (currentTabIndex == 2) // EXCEPTION 탭
            {
                // EXCEPTION 로그 프리셋 설정창 (현재 선택된 프리셋 이름 전달)
                var settingsWindow = new ExceptionPresetEditor(currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
                    
                    // 적용된 프리셋 선택
                    var appliedPresetName = AppSettingsManager.Settings.CurrentPresetName;
                    if (!string.IsNullOrEmpty(appliedPresetName))
                    {
                        logSettings.SelectPreset(appliedPresetName);
                    }
                }
            }
            else if (currentTabIndex == 3) // F/L 탭
            {
                // F/L 로그 프리셋 설정창
                var settingsWindow = new FLPresetEditor(currentPresetName);
                settingsWindow.Owner = this;
                settingsWindow.GetCurrentLogEntries = () => flLogViewer.GetLogEntries();
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();

                    // F/L 뷰어 탭 재생성 (프리셋 적용 반영)
                    flLogViewer.RefreshTabs();

                    System.Diagnostics.Debug.WriteLine($"✅ F/L 프리셋 설정 완료, 프리셋 목록 갱신 및 탭 재생성됨");
                }
            }
            else // EVENT 탭 (기본)
            {
                // EVENT 로그 컬럼 설정창 열기 (현재 선택된 프리셋 이름 전달)
                var settingsWindow = new EventPresetEditor("", currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
                    
                    // 적용된 프리셋 선택
                    var appliedPresetName = AppSettingsManager.Settings.CurrentPresetName;
                    if (!string.IsNullOrEmpty(appliedPresetName))
                    {
                        logSettings.SelectPreset(appliedPresetName);
                    }
                }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // 표시 옵션 저장 (실시간 감지 체크박스 등)
            logSettings.SaveDisplaySettings();
            
            // UserControl들의 리소스 정리
            eventLogViewer?.Cleanup();
            dataLogViewer?.Cleanup();
            exceptionLogViewer?.Cleanup();
            
            // 설정 저장
            AppSettingsManager.SaveCurrent();
        }

        /// <summary>
        /// 메인 탭 변경 시 AutoFit 적용 및 LogType 업데이트
        /// </summary>
        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 탭이 실제로 변경되었는지 확인 (이벤트 버블링 방지)
            if (e.Source != mainTabControl) return;

            // LogType 업데이트 (프리셋 버튼 색상 변경용)
            logSettings.LogType = mainTabControl.SelectedIndex switch
            {
                0 => LogType.EVENT,
                1 => LogType.DATA,
                2 => LogType.EXCEPTION,
                3 => LogType.FL,
                _ => LogType.EVENT
            };

            // 약간의 지연 후 AutoFit 적용 (UI 렌더링 완료 후)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (mainTabControl.SelectedIndex)
                {
                    case 0: // EVENT 로그 탭
                        eventLogViewer?.ApplyAutoFit();
                        break;
                    case 1: // DATA 로그 탭
                        dataLogViewer?.ApplyAutoFit();
                        break;
                    case 2: // EXCEPTION 로그 탭
                        exceptionLogViewer?.ApplyAutoFit();
                        break;
                    case 3: // F/L 로그 탭
                        flLogViewer?.ApplyAutoFit();
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
