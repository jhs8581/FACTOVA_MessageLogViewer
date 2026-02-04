using System.ComponentModel;
using System.IO;
using System.Windows;
using FACTOVA_MessageLogViewer.Models;

namespace FACTOVA_MessageLogViewer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
        private void LogSettings_StartViewerRequested(object? sender, System.EventArgs e)
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
            if (!string.IsNullOrEmpty(eventFilePath))
            {
                var eventSettings = viewerSettings with { LogFilePath = eventFilePath, LogType = LogType.EVENT };
                eventLogViewer.Initialize(eventSettings);
            }

            // DATA 로그 파일 경로 찾기
            var dataFilePath = FindLogFilePath(selectedDate.Date, LogType.DATA);
            if (!string.IsNullOrEmpty(dataFilePath))
            {
                var dataSettings = viewerSettings with { LogFilePath = dataFilePath, LogType = LogType.DATA };
                dataLogViewer.Initialize(dataSettings);
            }

            // EXCEPTION 로그 파일 경로 찾기
            var exceptionFilePath = FindLogFilePath(selectedDate.Date, LogType.EXCEPTION);
            if (!string.IsNullOrEmpty(exceptionFilePath))
            {
                var exceptionSettings = viewerSettings with { LogFilePath = exceptionFilePath, LogType = LogType.EXCEPTION };
                exceptionLogViewer.Initialize(exceptionSettings);
            }

            // 설정 영역 접기
            logSettings.CollapseExpander();

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
            
            if (presetName == "Default")
            {
                // 기본 프리셋 사용
                UnifiedPresetManager.CurrentPreset = UnifiedPreset.CreateDefault();
            }
            else
            {
                // 저장된 프리셋 로드
                var preset = UnifiedPresetManager.LoadPreset(presetName);
                if (preset != null)
                {
                    UnifiedPresetManager.CurrentPreset = preset;
                }
                else
                {
                    UnifiedPresetManager.CurrentPreset = UnifiedPreset.CreateDefault();
                }
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
                var settingsWindow = new DataColumnSettingsWindow(currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
                }
            }
            else if (currentTabIndex == 2) // EXCEPTION 탭
            {
                // EXCEPTION 로그 프리셋 설정창 (현재 선택된 프리셋 이름 전달)
                var settingsWindow = new ExceptionColumnSettingsWindow(currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
                }
            }
            else // EVENT 탭 (기본)
            {
                // EVENT 로그 컬럼 설정창 열기 (현재 선택된 프리셋 이름 전달)
                var settingsWindow = new ColumnSettingsWindow("", currentPresetName);
                settingsWindow.Owner = this;
                if (settingsWindow.ShowDialog() == true)
                {
                    // 프리셋 목록 갱신
                    logSettings.LoadPresetList();
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
        /// 메인 탭 변경 시 AutoFit 적용
        /// </summary>
        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // 탭이 실제로 변경되었는지 확인 (이벤트 버블링 방지)
            if (e.Source != mainTabControl) return;

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
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
