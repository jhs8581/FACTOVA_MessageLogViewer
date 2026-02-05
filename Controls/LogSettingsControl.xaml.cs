using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer.Controls
{
    /// <summary>
    /// 공통 로그 설정 컨트롤
    /// EVENT/DATA 로그 뷰어에서 공유
    /// </summary>
    public partial class LogSettingsControl : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty LogTypeProperty =
            DependencyProperty.Register("LogType", typeof(LogType), typeof(LogSettingsControl),
                new PropertyMetadata(LogType.EVENT, OnLogTypeChanged));

        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register("AccentColor", typeof(string), typeof(LogSettingsControl),
                new PropertyMetadata("#4CAF50", OnAccentColorChanged));

        public static readonly DependencyProperty ShowPresetPanelProperty =
            DependencyProperty.Register("ShowPresetPanel", typeof(bool), typeof(LogSettingsControl),
                new PropertyMetadata(true, OnShowPresetPanelChanged));

        public static readonly DependencyProperty ShowSlowQueryFilterProperty =
            DependencyProperty.Register("ShowSlowQueryFilter", typeof(bool), typeof(LogSettingsControl),
                new PropertyMetadata(false, OnShowSlowQueryFilterChanged));

        public LogType LogType
        {
            get => (LogType)GetValue(LogTypeProperty);
            set => SetValue(LogTypeProperty, value);
        }

        public string AccentColor
        {
            get => (string)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }

        public bool ShowPresetPanel
        {
            get => (bool)GetValue(ShowPresetPanelProperty);
            set => SetValue(ShowPresetPanelProperty, value);
        }

        public bool ShowSlowQueryFilter
        {
            get => (bool)GetValue(ShowSlowQueryFilterProperty);
            set => SetValue(ShowSlowQueryFilterProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler? StartViewerRequested;
        public event EventHandler? FolderChanged;
        public event EventHandler? DateRefreshRequested;
        public event EventHandler<string>? PresetChanged;
        public event EventHandler? PresetSettingsRequested;

        #endregion

        #region Properties

        public string CurrentLogDirectory { get; private set; } = "";
        public bool IsDefaultFolder { get; private set; } = true;
        public ObservableCollection<AvailableDate> AvailableDates { get; } = new();

        public AvailableDate? SelectedDate => cboAvailableDates.SelectedItem as AvailableDate;

        public LogLoadMode LoadMode =>
            rbNewOnly.IsChecked == true ? LogLoadMode.NewOnly :
            rbLoadRecent.IsChecked == true ? LogLoadMode.Recent : LogLoadMode.All;

        public int RecentCount =>
            int.TryParse(txtRecentCount.Text, out int count) && count > 0 ? count : 1000;

        public TimeSpan FilterStartTime =>
            TimeSpan.TryParse(txtStartTime.Text + ":00", out var time) ? time : TimeSpan.Zero;

        public TimeSpan FilterEndTime =>
            TimeSpan.TryParse(txtEndTime.Text + ":59", out var time) ? time : new TimeSpan(23, 59, 59);

        public bool SlowQueryOnly => chkSlowQueryOnly.IsChecked == true;

        public bool WatchEventLog => chkWatchEvent.IsChecked == true;
        public bool WatchDataLog => chkWatchData.IsChecked == true;
        public bool WatchExceptionLog => chkWatchException.IsChecked == true;
        public bool WatchFLLog => chkWatchFL.IsChecked == true;

        /// <summary>
        /// F/L 로그 별도 폴더 사용 여부
        /// </summary>
        public bool UseSeparateFLFolder => rbFLUseSeparateFolder.IsChecked == true;

        /// <summary>
        /// F/L 로그 폴더 경로 (별도 폴더 사용 시)
        /// </summary>
        public string FLLogFolderPath => txtFLLogFolder.Text?.Trim() ?? "";

        public string LogFolderPath
        {
            get => txtLogFolderPath.Text;
            set => txtLogFolderPath.Text = value;
        }

        #endregion

        public LogSettingsControl()
        {
            InitializeComponent();
            cboAvailableDates.ItemsSource = AvailableDates;
            
            // Loaded 이벤트에서 저장된 설정 불러오기
            Loaded += LogSettingsControl_Loaded;
        }

        private void LogSettingsControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDisplaySettings();
        }

        #region 표시 옵션 저장/로드

        /// <summary>
        /// 저장된 표시 옵션 불러오기
        /// </summary>
        public void LoadDisplaySettings()
        {
            try
            {
                var settings = AppSettingsManager.Settings;
                
                // 로드 모드 설정
                switch (settings.LogLoadMode)
                {
                    case 0:
                        rbNewOnly.IsChecked = true;
                        break;
                    case 1:
                        rbLoadRecent.IsChecked = true;
                        break;
                    case 2:
                        rbLoadAll.IsChecked = true;
                        break;
                    default:
                        rbNewOnly.IsChecked = true;
                        break;
                }
                
                // 최근 로그 개수
                if (settings.RecentLogCount > 0)
                {
                    txtRecentCount.Text = settings.RecentLogCount.ToString();
                }
                else
                {
                    txtRecentCount.Text = "1000";
                }
                
                // 실시간 감지 설정
                chkWatchEvent.IsChecked = settings.WatchEventLog;
                chkWatchData.IsChecked = settings.WatchDataLog;
                chkWatchException.IsChecked = settings.WatchExceptionLog;

                // F/L 로그 설정
                rbFLUseLogFolder.IsChecked = !settings.UseSeparateFLFolder;
                rbFLUseSeparateFolder.IsChecked = settings.UseSeparateFLFolder;
                txtFLLogFolder.Text = settings.FLLogFolder ?? "";
                chkWatchFL.IsChecked = settings.WatchFLLog;
                
                System.Diagnostics.Debug.WriteLine($"📋 표시 옵션 로드: LoadMode={settings.LogLoadMode}, Watch=[E:{settings.WatchEventLog}, D:{settings.WatchDataLog}, X:{settings.WatchExceptionLog}, FL:{settings.WatchFLLog}]");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 표시 옵션 로드 실패: {ex.Message}");
                
                // 기본값 설정
                rbNewOnly.IsChecked = true;
                txtRecentCount.Text = "1000";
                chkWatchEvent.IsChecked = true;
                chkWatchData.IsChecked = true;
                chkWatchException.IsChecked = true;
                rbFLUseLogFolder.IsChecked = true;
                chkWatchFL.IsChecked = false;
            }
        }

        /// <summary>
        /// 현재 표시 옵션 저장
        /// </summary>
        public void SaveDisplaySettings()
        {
            try
            {
                var settings = AppSettingsManager.Settings;
                
                // 로드 모드 저장
                settings.LogLoadMode = rbNewOnly.IsChecked == true ? 0 :
                                       rbLoadRecent.IsChecked == true ? 1 : 2;
                
                // 최근 로그 개수 저장
                if (int.TryParse(txtRecentCount.Text, out int count) && count > 0)
                {
                    settings.RecentLogCount = count;
                }
                
                // 실시간 감지 설정 저장
                settings.WatchEventLog = chkWatchEvent.IsChecked == true;
                settings.WatchDataLog = chkWatchData.IsChecked == true;
                settings.WatchExceptionLog = chkWatchException.IsChecked == true;

                // F/L 로그 설정 저장
                settings.UseSeparateFLFolder = rbFLUseSeparateFolder.IsChecked == true;
                settings.FLLogFolder = txtFLLogFolder.Text?.Trim() ?? "";
                settings.WatchFLLog = chkWatchFL.IsChecked == true;
                
                AppSettingsManager.SaveCurrent();
                
                System.Diagnostics.Debug.WriteLine($"💾 표시 옵션 저장: LoadMode={settings.LogLoadMode}, Watch=[E:{settings.WatchEventLog}, D:{settings.WatchDataLog}, X:{settings.WatchExceptionLog}, FL:{settings.WatchFLLog}]");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 표시 옵션 저장 실패: {ex.Message}");
            }
        }

        #endregion

        #region Property Changed Callbacks

        private static void OnLogTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogSettingsControl ctrl)
            {
                ctrl.UpdateForLogType();
            }
        }

        private static void OnAccentColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogSettingsControl ctrl && e.NewValue is string color)
            {
                try
                {
                    var brush = new System.Windows.Media.BrushConverter().ConvertFromString(color) as System.Windows.Media.Brush;
                    if (brush != null)
                    {
                        ctrl.btnStartViewer.Background = brush;
                    }
                }
                catch { }
            }
        }

        private static void OnShowPresetPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogSettingsControl ctrl)
            {
                ctrl.presetPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static void OnShowSlowQueryFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LogSettingsControl ctrl)
            {
                ctrl.chkSlowQueryOnly.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateForLogType()
        {
            // 로그 타입에 따른 UI 조정
            if (LogType == LogType.DATA)
            {
                ShowPresetPanel = false;
                ShowSlowQueryFilter = true;
                AccentColor = "#FF6B00";
            }
            else
            {
                ShowPresetPanel = true;
                ShowSlowQueryFilter = false;
                AccentColor = "#4CAF50";
            }
        }

        #endregion

        #region Public Methods

        public void SetDefaultFolder(string defaultFolder)
        {
            CurrentLogDirectory = defaultFolder;
            IsDefaultFolder = true;
            txtLogFolderPath.Text = defaultFolder;
        }

        public void SetCustomFolder(string folder)
        {
            CurrentLogDirectory = folder;
            IsDefaultFolder = false;
            txtLogFolderPath.Text = folder;
        }

        public void RefreshAvailableDates()
        {
            AvailableDates.Clear();
            if (string.IsNullOrEmpty(CurrentLogDirectory)) return;

            var dates = IsDefaultFolder
                ? FindDatesInDefaultFolder(CurrentLogDirectory)
                : FindDatesInCustomFolder(CurrentLogDirectory);

            foreach (var d in dates.OrderByDescending(x => x.Date))
                AvailableDates.Add(d);

            if (AvailableDates.Count > 0)
            {
                cboAvailableDates.SelectedIndex = 0;
                txtDateInfo.Text = $"총 {AvailableDates.Count}개의 {(LogType == LogType.DATA ? "DATA" : "EVENT")} 로그 파일";
            }
            else
            {
                txtDateInfo.Text = "로그 파일을 찾을 수 없습니다.";
            }
        }


        /// <summary>
        /// 프리셋 목록 로드 (통합 프리셋)
        /// </summary>
        public void LoadPresetList()
        {
            try
            {
                cboPresets.Items.Clear();
                cboPresets.Items.Add("Default");

                // 통합 프리셋 목록 가져오기
                var presetNames = UnifiedPresetManager.GetPresetNames();
                System.Diagnostics.Debug.WriteLine($"🎨 프리셋 목록 로드: {presetNames.Count}개 발견");
                
                foreach (var name in presetNames)
                {
                    System.Diagnostics.Debug.WriteLine($"   - {name}");
                    cboPresets.Items.Add(name);
                }

                // 저장된 프리셋 이름 선택
                var savedPresetName = AppSettingsManager.Settings.CurrentPresetName;
                System.Diagnostics.Debug.WriteLine($"🎨 저장된 프리셋: {savedPresetName}");
                
                int matchIndex = 0;

                if (!string.IsNullOrEmpty(savedPresetName))
                {
                    for (int i = 0; i < cboPresets.Items.Count; i++)
                    {
                        if (cboPresets.Items[i]?.ToString() == savedPresetName)
                        {
                            matchIndex = i;
                            break;
                        }
                    }
                }

                cboPresets.SelectedIndex = matchIndex;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 프리셋 목록 로드 실패: {ex.Message}");
            }
        }

        public void LoadPresets(System.Collections.IEnumerable presets)
        {
            cboPresets.ItemsSource = presets;
        }

        public void SelectPreset(string presetName)
        {
            foreach (var item in cboPresets.Items)
            {
                if (item?.ToString() == presetName)
                {
                    cboPresets.SelectedItem = item;
                    break;
                }
            }
        }

        /// <summary>
        /// 현재 선택된 프리셋 이름
        /// </summary>
        public string SelectedPresetName => cboPresets.SelectedItem?.ToString() ?? "Default";

        public void CollapseExpander()
        {
            expanderSettings.IsExpanded = false;
        }

        public void ExpandExpander()
        {
            expanderSettings.IsExpanded = true;
        }

        #endregion

        #region Event Handlers

        private void BtnSetDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            var defaultFolder = AppSettingsManager.Settings.DefaultLogFolder;
            if (!string.IsNullOrEmpty(defaultFolder) && Directory.Exists(defaultFolder))
            {
                SetDefaultFolder(defaultFolder);
                RefreshAvailableDates();
                FolderChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "로그 폴더를 선택하세요",
                InitialDirectory = CurrentLogDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                SetCustomFolder(dialog.FolderName);
                RefreshAvailableDates();
                FolderChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenCurrentFolder();
        }

        private void TxtLogFolderPath_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenCurrentFolder();
        }

        private void OpenCurrentFolder()
        {
            var folderPath = txtLogFolderPath.Text;
            if (string.IsNullOrEmpty(folderPath))
                return;

            try
            {
                if (Directory.Exists(folderPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = folderPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show($"폴더가 존재하지 않습니다:\n{folderPath}", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더를 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefreshDates_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailableDates();
            DateRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboPresets.SelectedItem != null)
            {
                PresetChanged?.Invoke(this, cboPresets.SelectedItem.ToString() ?? "");
            }
        }

        private void BtnColumnSettings_Click(object sender, RoutedEventArgs e)
        {
            PresetSettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CboTimeRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtStartTime == null || txtEndTime == null) return;
            if (cboTimeRange.SelectedItem is ComboBoxItem item)
            {
                var content = item.Content?.ToString() ?? "";
                (txtStartTime.Text, txtEndTime.Text) = content switch
                {
                    "오전" => ("06:00", "11:59"),
                    "오후" => ("12:00", "17:59"),
                    "잔업" => ("18:00", "23:59"),
                    var h when h.EndsWith("시") && int.TryParse(h.Replace("시", ""), out int hour) =>
                        ($"{hour:D2}:00", $"{hour:D2}:59"),
                    _ => ("00:00", "23:59")
                };
            }
        }

        private void BtnStartViewer_Click(object sender, RoutedEventArgs e)
        {
            // 유효성 검사
            if (SelectedDate == null)
            {
                MessageBox.Show("날짜를 선택해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(SelectedDate.FilePath))
            {
                MessageBox.Show($"로그 파일이 존재하지 않습니다:\n{SelectedDate.FilePath}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (LoadMode == LogLoadMode.Recent && RecentCount <= 0)
            {
                MessageBox.Show("개수는 1 이상의 숫자를 입력해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartViewerRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnBrowseFLFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "F/L 로그 폴더를 선택하세요",
                InitialDirectory = !string.IsNullOrEmpty(txtFLLogFolder.Text) && Directory.Exists(txtFLLogFolder.Text)
                    ? txtFLLogFolder.Text
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == true)
            {
                txtFLLogFolder.Text = dialog.FolderName;
            }
        }

        private void RbFLFolder_Checked(object sender, RoutedEventArgs e)
        {
            // F/L 폴더 옵션 변경 시 처리 (필요 시)
        }

        #endregion

        #region Private Methods - Date Finding

        private System.Collections.Generic.List<AvailableDate> FindDatesInDefaultFolder(string baseDir)
        {
            var result = new System.Collections.Generic.List<AvailableDate>();
            if (!Directory.Exists(baseDir)) return result;

            var filePattern = LogType == LogType.DATA
                ? new Regex(@"LGE GMES_DATA_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase)
                : new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                foreach (var yearDir in Directory.GetDirectories(baseDir))
                {
                    if (!int.TryParse(Path.GetFileName(yearDir), out int year) || year < 2000 || year > 2100) continue;

                    foreach (var monthDir in Directory.GetDirectories(yearDir))
                    {
                        if (!int.TryParse(Path.GetFileName(monthDir), out int month) || month < 1 || month > 12) continue;

                        foreach (var file in Directory.GetFiles(monthDir, "*.log"))
                        {
                            var match = filePattern.Match(Path.GetFileName(file));
                            if (match.Success)
                            {
                                try
                                {
                                    var date = new DateTime(
                                        int.Parse(match.Groups[3].Value),
                                        int.Parse(match.Groups[1].Value),
                                        int.Parse(match.Groups[2].Value));
                                    result.Add(new AvailableDate { Date = date, FilePath = file });
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private System.Collections.Generic.List<AvailableDate> FindDatesInCustomFolder(string folder)
        {
            var result = new System.Collections.Generic.List<AvailableDate>();
            if (!Directory.Exists(folder)) return result;

            var filePattern = LogType == LogType.DATA
                ? new Regex(@"LGE GMES_DATA_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase)
                : new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.log", SearchOption.AllDirectories))
                {
                    var match = filePattern.Match(Path.GetFileName(file));
                    if (match.Success)
                    {
                        try
                        {
                            var date = new DateTime(
                                int.Parse(match.Groups[3].Value),
                                int.Parse(match.Groups[1].Value),
                                int.Parse(match.Groups[2].Value));
                            result.Add(new AvailableDate { Date = date, FilePath = file });
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return result;
        }

        #endregion
    }
}
