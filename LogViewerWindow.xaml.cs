using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace FACTOVA_MessageLogViewer
{
    public partial class LogViewerWindow : Window
    {
        private LogViewerManager logManager = null!;
        private ObservableCollection<LogEntry> logEntries = null!;
        private ObservableCollection<LogEntry> displayEntries = null!;
        private ICollectionView logView = null!;

        private FileSystemWatcher? fileWatcher;
        private string logDirectory = "";
        private string currentLogFile = "";
        private long lastPosition = 0;

        private bool isPaused = false;
        private List<LogEntry> pausedBuffer = new List<LogEntry>();


        private LogLoadMode loadMode;
        private int recentCount;
        private DateTime selectedDate;

        // 탭별 DataGrid 및 View 관리
        private Dictionary<TabConfig, DataGrid> tabDataGrids = new();
        private Dictionary<TabConfig, ICollectionView> tabViews = new();
        private Dictionary<TabConfig, ObservableCollection<LogEntry>> tabDisplayEntries = new();
        private DataGrid? currentDataGrid;
        private TabConfig? currentTabConfig;

        // 멀티라인 파싱용 버퍼
        private StringBuilder multiLineBuffer = new StringBuilder();
        // 로그 시작 패턴: [MM-DD-YYYY HH:mm:ss.fff][RECV|SENDDATA|RECVDATA] 형식만 매칭
        // System : 같은 일반 로그는 무시
        private static readonly Regex LogStartPattern = new Regex(
            @"^\[(\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]\[([A-Z]+)\]", 
            RegexOptions.Compiled | RegexOptions.Multiline);

        // 디바운싱용
        private System.Threading.Timer? debounceTimer;
        private readonly object fileLock = new object();
        private bool isReading = false;

        // 설정 관련
        private string currentLogDirectory = "";
        private bool isDefaultFolder = true;
        private ObservableCollection<AvailableDate> availableDates = new();
        private int lastSelectedTabIndex = 0;
        
        // 자동 스크롤 설정
        private bool isAutoScrollEnabled = true;

        // 시간대 필터
        private TimeSpan filterStartTime = TimeSpan.Zero;
        private TimeSpan filterEndTime = new TimeSpan(23, 59, 59);

        // 기본 생성자 (설정 화면으로 시작)
        public LogViewerWindow()
        {
            InitializeComponent();

            cboAvailableDates.ItemsSource = availableDates;

            // 저장된 설정 로드
            LoadConfig();

            // 날짜 목록 갱신
            RefreshAvailableDates();

            // 설정 영역 펼쳐진 상태로 시작
            expanderSettings.IsExpanded = true;

            LoadPresetList();
        }

        // 기존 생성자 (MainWindow에서 호출용, 이제는 사용 안함)
        public LogViewerWindow(string logFilePath, DateTime date, LogLoadMode mode, int count)
        {
            InitializeComponent();

            currentLogFile = logFilePath;
            logDirectory = Path.GetDirectoryName(logFilePath) ?? "";
            selectedDate = date;
            loadMode = mode;
            recentCount = count;
            currentLogDirectory = logDirectory;

            cboAvailableDates.ItemsSource = availableDates;
            txtLogFolder.Text = $"({Path.GetFileName(logFilePath)})";

            // 설정 영역 접기
            expanderSettings.IsExpanded = false;

            LoadPresetList();             // 프리셋 목록 로드
            InitializeLogManager();
            InitializeTabs();             // 탭 초기화 (동적 컬럼 포함)
            LoadSavedFontSize();          // 저장된 폰트 크기 로드
            StartFileWatcher();
            LoadLogs();

            UpdateModeText();
        }


        /// <summary>
        /// 프리셋 목록 로드
        /// </summary>
        private void LoadPresetList()
        {
            isLoadingPreset = true;
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");
            
            foreach (var preset in ColumnSettingsManager.GetPresetNames())
            {
                cboPresets.Items.Add(preset);
            }

            // 현재 설정의 이름과 일치하는 프리셋 선택
            var currentName = ColumnSettingsManager.CurrentSettings.Name;
            var matchIndex = -1;
            for (int i = 0; i < cboPresets.Items.Count; i++)
            {
                if (cboPresets.Items[i]?.ToString() == currentName)
                {
                    matchIndex = i;
                    break;
                }
            }
            
            cboPresets.SelectedIndex = matchIndex >= 0 ? matchIndex : 0;
            isLoadingPreset = false;
        }

        /// <summary>
        /// 설정 로드 (MainWindow에서 가져옴)
        /// </summary>
        private void LoadConfig()
        {
            var settings = AppSettingsManager.Settings;
            
            // 마지막 사용 폴더가 있으면 해당 폴더로 시작
            if (!string.IsNullOrEmpty(settings.LastUsedFolder) && Directory.Exists(settings.LastUsedFolder))
            {
                currentLogDirectory = settings.LastUsedFolder;
                isDefaultFolder = settings.LastUsedFolder.Equals(settings.DefaultLogFolder, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                currentLogDirectory = settings.DefaultLogFolder;
                isDefaultFolder = true;
            }
            
            txtLogFolderPath.Text = currentLogDirectory;

            // 로그 로드 모드 복원
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

            // 최근 로그 개수 복원
            if (settings.RecentLogCount > 0)
            {
                txtRecentCount.Text = settings.RecentLogCount.ToString();
            }
        }

        /// <summary>
        /// 설정 저장
        /// </summary>
        private void SaveConfig()
        {
            var settings = AppSettingsManager.Settings;
            settings.LastUsedFolder = currentLogDirectory;

            // 로그 로드 모드 저장
            if (rbNewOnly.IsChecked == true)
                settings.LogLoadMode = 0;
            else if (rbLoadRecent.IsChecked == true)
                settings.LogLoadMode = 1;
            else if (rbLoadAll.IsChecked == true)
                settings.LogLoadMode = 2;

            // 최근 로그 개수 저장
            if (int.TryParse(txtRecentCount.Text, out int count) && count > 0)
            {
                settings.RecentLogCount = count;
            }

            AppSettingsManager.SaveCurrent();
        }

        /// <summary>
        /// 사용 가능한 날짜 목록 갱신 (MainWindow에서 가져옴)
        /// </summary>
        private void RefreshAvailableDates()
        {
            availableDates.Clear();

            var dates = new List<AvailableDate>();

            if (isDefaultFolder)
            {
                // 기본폴더: 년/월 구조 검색
                dates = FindDatesInDefaultFolder(currentLogDirectory);
            }
            else
            {
                // 사용자 폴더: 해당 폴더 내 로그 파일 검색
                dates = FindDatesInCustomFolder(currentLogDirectory);
            }

            // 최신 날짜 순으로 정렬
            foreach (var date in dates.OrderByDescending(d => d.Date))
            {
                availableDates.Add(date);
            }

            // 첫 번째 (최신) 선택
            if (availableDates.Count > 0)
            {
                cboAvailableDates.SelectedIndex = 0;
                txtDateInfo.Text = $"총 {availableDates.Count}개의 로그 파일을 찾았습니다.";
            }
            else
            {
                txtDateInfo.Text = "로그 파일을 찾을 수 없습니다.";
            }
        }

        /// <summary>
        /// 기본폴더(년/월 구조)에서 로그 파일 검색
        /// </summary>
        private List<AvailableDate> FindDatesInDefaultFolder(string baseDir)
        {
            var result = new List<AvailableDate>();
            
            if (!Directory.Exists(baseDir))
                return result;

            // LGE GMES_EVENT_MMDDYYYY.log 패턴
            var filePattern = new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                // 년 폴더들 검색
                foreach (var yearDir in Directory.GetDirectories(baseDir))
                {
                    string yearName = Path.GetFileName(yearDir);
                    if (!int.TryParse(yearName, out int year) || year < 2000 || year > 2100)
                        continue;

                    // 월 폴더들 검색
                    foreach (var monthDir in Directory.GetDirectories(yearDir))
                    {
                        string monthName = Path.GetFileName(monthDir);
                        if (!int.TryParse(monthName, out int month) || month < 1 || month > 12)
                            continue;

                        // 로그 파일들 검색
                        foreach (var file in Directory.GetFiles(monthDir, "*.log"))
                        {
                            var match = filePattern.Match(Path.GetFileName(file));
                            if (match.Success)
                            {
                                int mm = int.Parse(match.Groups[1].Value);
                                int dd = int.Parse(match.Groups[2].Value);
                                int yyyy = int.Parse(match.Groups[3].Value);

                                try
                                {
                                    var date = new DateTime(yyyy, mm, dd);
                                    result.Add(new AvailableDate { Date = date, FilePath = file });
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴더 검색 오류: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 사용자 지정 폴더에서 로그 파일 검색
        /// </summary>
        private List<AvailableDate> FindDatesInCustomFolder(string folder)
        {
            var result = new List<AvailableDate>();

            if (!Directory.Exists(folder))
                return result;

            var filePattern = new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                // 현재 폴더와 하위 폴더에서 검색
                foreach (var file in Directory.GetFiles(folder, "*.log", SearchOption.AllDirectories))
                {
                    var match = filePattern.Match(Path.GetFileName(file));
                    if (match.Success)
                    {
                        int mm = int.Parse(match.Groups[1].Value);
                        int dd = int.Parse(match.Groups[2].Value);
                        int yyyy = int.Parse(match.Groups[3].Value);

                        try
                        {
                            var date = new DateTime(yyyy, mm, dd);
                            result.Add(new AvailableDate { Date = date, FilePath = file });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴더 검색 오류: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 설정에 따라 로그 뷰어 로드
        /// </summary>
        private void LoadLogViewerWithSettings()
        {
            // 선택된 날짜 확인
            var selectedItem = cboAvailableDates.SelectedItem as AvailableDate;
            if (selectedItem == null)
            {
                MessageBox.Show(
                    "로그 날짜를 선택해주세요.",
                    "알림",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            string logFilePath = selectedItem.FilePath;
            selectedDate = selectedItem.Date;

            System.Diagnostics.Debug.WriteLine($"📅 선택된 날짜: {selectedDate:yyyy-MM-dd}");
            System.Diagnostics.Debug.WriteLine($"📄 선택된 파일: {logFilePath}");

            // 로그 파일 확인
            if (!File.Exists(logFilePath))
            {
                MessageBox.Show(
                    $"로그 파일이 존재하지 않습니다:\n{logFilePath}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            // 옵션 확인
            loadMode = LogLoadMode.NewOnly;
            recentCount = 500;

            if (rbNewOnly.IsChecked == true)
            {
                loadMode = LogLoadMode.NewOnly;
            }
            else if (rbLoadRecent.IsChecked == true)
            {
                loadMode = LogLoadMode.Recent;
                if (!int.TryParse(txtRecentCount.Text, out recentCount) || recentCount <= 0)
                {
                    MessageBox.Show("개수는 1 이상의 숫자를 입력해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (rbLoadAll.IsChecked == true)
            {
                loadMode = LogLoadMode.All;
                
                // 시간대 필터 설정
                if (TimeSpan.TryParse(txtStartTime.Text + ":00", out var startTime))
                {
                    filterStartTime = startTime;
                }
                if (TimeSpan.TryParse(txtEndTime.Text + ":59", out var endTime))
                {
                    filterEndTime = endTime;
                }
            }

            // 로그 뷰어 초기화
            currentLogFile = logFilePath;
            logDirectory = Path.GetDirectoryName(logFilePath) ?? "";
            txtLogFolder.Text = $"({Path.GetFileName(logFilePath)})";

            // 로그 파일에서 필드 목록 미리 추출
            var discoveredFields = LogFieldAnalyzer.ExtractFieldNames(logFilePath);
            LogFieldAnalyzer.AddDiscoveredFields(discoveredFields);
            System.Diagnostics.Debug.WriteLine($"🔍 발견된 필드: {discoveredFields.Count}개");

            InitializeLogManager();
            InitializeTabs();
            LoadSavedFontSize();
            StartFileWatcher();
            LoadLogs();

            UpdateModeText();

            // 설정 영역 접기
            expanderSettings.IsExpanded = false;

            // 설정 저장
            SaveConfig();

            // 로그 로드 완료 후 Auto Fit 자동 적용
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyAutoFit();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // DATA 로그 뷰어 열기
        private void BtnOpenDataLogViewer_Click(object sender, RoutedEventArgs e)
        {
            var dataLogViewer = new DataLogViewerWindow();
            dataLogViewer.Show();
        }

        // 이벤트 핸들러들
        private void BtnSetDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            currentLogDirectory = AppSettingsManager.Settings.DefaultLogFolder;
            isDefaultFolder = true;
            txtLogFolderPath.Text = currentLogDirectory;
            RefreshAvailableDates();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "로그 폴더를 선택하세요";
            dialog.InitialDirectory = currentLogDirectory;

            if (dialog.ShowDialog() == true)
            {
                currentLogDirectory = dialog.FolderName;
                isDefaultFolder = false;  // 사용자 지정 폴더
                txtLogFolderPath.Text = currentLogDirectory;
                
                // 폴더 경로 저장
                SaveConfig();
                
                RefreshAvailableDates();
            }
        }

        private void BtnRefreshDates_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailableDates();
        }

        private void BtnReloadLog_Click(object sender, RoutedEventArgs e)
        {
            // 기존 데이터 클리어
            if (logManager != null)
            {
                logManager.Clear();
            }

            // 로그 다시 로드
            LoadLogViewerWithSettings();
        }

        private void BtnStartViewer_Click(object sender, RoutedEventArgs e)
        {
            LoadLogViewerWithSettings();
        }

        private bool isLoadingPreset = false;

        /// <summary>
        /// 프리셋 선택 변경
        /// </summary>
        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPreset) return;

            var selectedPreset = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedPreset)) return;

            ColumnSettings? settings;
            if (selectedPreset == "Default")
            {
                settings = ColumnSettingsManager.CreateDefaultSettings();
            }
            else
            {
                settings = ColumnSettingsManager.LoadPreset(selectedPreset);
            }
            
            if (settings != null)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 프리셋 '{selectedPreset}' 로드");
                System.Diagnostics.Debug.WriteLine($"   - TabSettings: {settings.TabSettings != null}");
                System.Diagnostics.Debug.WriteLine($"   - Tabs count: {settings.TabSettings?.Tabs?.Count ?? 0}");
                System.Diagnostics.Debug.WriteLine($"   - EnabledTabs count: {settings.TabSettings?.EnabledTabs?.Count() ?? 0}");
                
                // 현재 설정으로 적용 (이름도 저장됨)
                ColumnSettingsManager.CurrentSettings = settings;

                // 탭 재초기화
                InitializeTabs();
                ReloadExistingLogs();
                
                
                // 폰트 크기 적용
                LoadSavedFontSize();
            }
            // 콤보박스는 선택한 프리셋 유지
        }

        /// <summary>
        /// 탭 초기화 (동적 생성)
        /// </summary>
        private void InitializeTabs()
        {
            tabControlLogs.Items.Clear();
            tabDataGrids.Clear();
            tabViews.Clear();
            tabDisplayEntries.Clear();

            var settings = ColumnSettingsManager.CurrentSettings;
            
            System.Diagnostics.Debug.WriteLine($"📋 InitializeTabs 호출");
            System.Diagnostics.Debug.WriteLine($"   - TabSettings: {settings.TabSettings != null}");
            System.Diagnostics.Debug.WriteLine($"   - Tabs count: {settings.TabSettings?.Tabs?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"   - EnabledTabs count: {settings.TabSettings?.EnabledTabs?.Count() ?? 0}");
            
            var tabs = settings.TabSettings?.EnabledTabs?.ToList() ?? new List<TabConfig>();

            // 탭이 없으면 기본 통합 탭 생성
            if (tabs.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ 탭이 없어서 기본 탭 생성");
                tabs.Add(new TabConfig
                {
                    Name = "📊 통합 로그",
                    IsIntegrated = true,
                    IsEnabled = true
                });
            }

            foreach (var tabConfig in tabs)
            {
                var tabItem = CreateTabItem(tabConfig);
                tabControlLogs.Items.Add(tabItem);
            }

            // 첫 번째 탭 선택
            if (tabControlLogs.Items.Count > 0)
            {
                tabControlLogs.SelectedIndex = Math.Min(
                    settings.TabSettings?.LastSelectedTabIndex ?? 0,
                    tabControlLogs.Items.Count - 1
                );
            }
        }

        /// <summary>
        /// 개별 탭 아이템 생성
        /// </summary>
        private TabItem CreateTabItem(TabConfig tabConfig)
        {
            // 탭별 데이터 컬렉션 생성
            var entries = new ObservableCollection<LogEntry>();
            tabDisplayEntries[tabConfig] = entries;

            // DataGrid 생성
            var dataGrid = CreateDataGrid(tabConfig, entries);
            tabDataGrids[tabConfig] = dataGrid;

            // View 생성 및 필터 설정
            var view = CollectionViewSource.GetDefaultView(entries);
            
            // 필터가 있을 때만 적용 (성능 최적화)
            if (!string.IsNullOrWhiteSpace(txtSearch?.Text) || 
                chkSendOnly?.IsChecked == true || 
                chkRecvOnly?.IsChecked == true)
            {
                view.Filter = item => FilterLogEntry(item, tabConfig);
            }
            
            tabViews[tabConfig] = view;

            dataGrid.ItemsSource = view;

            // 탭 헤더 (카운트 표시 포함)
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var headerText = new TextBlock 
            { 
                Text = tabConfig.Name,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Medium
            };
            var countText = new TextBlock 
            { 
                Text = "(0)", 
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),  // 파란색
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            headerPanel.Children.Add(headerText);
            headerPanel.Children.Add(countText);

            // 툴팁에 조건 표시
            var tooltip = string.IsNullOrEmpty(tabConfig.ConditionSummary) 
                ? tabConfig.Name 
                : $"{tabConfig.Name}\n조건: {tabConfig.ConditionSummary}";

            var tabItem = new TabItem
            {
                Header = headerPanel,
                Content = dataGrid,
                Tag = tabConfig,
                ToolTip = tooltip
            };

            return tabItem;
        }

        /// <summary>
        /// DataGrid 생성 (ListView보다 성능 우수)
        /// </summary>
        private DataGrid CreateDataGrid(TabConfig tabConfig, ObservableCollection<LogEntry> entries)
        {
            var dataGrid = new DataGrid
            {
                FontSize = 11,
                Margin = new Thickness(0),
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,  // 행 전체 선택
                ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,  // 복사 시 헤더 제외
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                CanUserSortColumns = true,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };

            // 더블클릭 이벤트 (상세 보기)
            dataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            
            // 키보드 이벤트 (Ctrl+C 셀 값 복사)
            dataGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;

            // 가상화 설정 (휠 스크롤 최적화)
            VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
            VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetCacheLength(dataGrid, new VirtualizationCacheLength(10, 10));  // 캐시 축소로 스크롤 속도 향상
            VirtualizingPanel.SetCacheLengthUnit(dataGrid, VirtualizationCacheLengthUnit.Item);
            VirtualizingPanel.SetScrollUnit(dataGrid, ScrollUnit.Item);  // 항목 단위 스크롤 (중요!)
            
            // 스크롤 최적화
            ScrollViewer.SetIsDeferredScrollingEnabled(dataGrid, false);
            ScrollViewer.SetCanContentScroll(dataGrid, true);  // 픽셀이 아닌 항목 단위
            ScrollViewer.SetHorizontalScrollBarVisibility(dataGrid, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(dataGrid, ScrollBarVisibility.Auto);

            // ROW 번호 컬럼 (5자리 고정 - 99999까지 표시 가능)
            var rowNumberColumn = CreateDataGridTextColumn("#", "RowNumber", 60, HorizontalAlignment.Center, "Consolas", "#666666");
            rowNumberColumn.CanUserResize = false;  // 크기 조정 불가
            rowNumberColumn.CanUserSort = false;    // 정렬 불가
            dataGrid.Columns.Add(rowNumberColumn);

            // 기본 컬럼들
            dataGrid.Columns.Add(CreateDataGridTextColumn("시간", "TimeString", 100, HorizontalAlignment.Left, "Consolas"));
            dataGrid.Columns.Add(CreateDataGridTextColumn("구분", "DirectionText", 50, HorizontalAlignment.Center, null, null, FontWeights.Bold));
            dataGrid.Columns.Add(CreateDataGridTextColumn("MsgId", "MessageId", 60, HorizontalAlignment.Center, "Consolas"));

            // 통합 로그 탭에만 "분류" 컬럼 추가
            if (tabConfig.IsIntegrated)
            {
                dataGrid.Columns.Add(CreateDataGridTextColumn("분류", "MatchedTabName", 100, HorizontalAlignment.Left, null, "#1565C0"));
            }

            // 동적 컬럼 추가 (탭별 필터링)
            var settings = ColumnSettingsManager.CurrentSettings;
            foreach (var fieldConfig in settings.ColumnFields)
            {
                // 이 컬럼이 현재 탭에서 표시되어야 하는지 확인
                if (fieldConfig.IsVisibleInTab(tabConfig.Name))
                {
                    var column = CreateDataGridDynamicColumn(fieldConfig);
                    dataGrid.Columns.Add(column);
                }
            }

            // Summary 컬럼 (항상 마지막)
            dataGrid.Columns.Add(CreateDataGridTextColumn("주요내용", "Summary", 400));

            // DataGrid Row 스타일 (배경색)
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new Binding("BackgroundBrush") { Mode = BindingMode.OneTime }));
            rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 28.0));
            dataGrid.RowStyle = rowStyle;

            // 헤더 스타일
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 13.0));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 240, 240))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 8, 8, 8)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 220, 220))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            dataGrid.ColumnHeaderStyle = headerStyle;

            // Cell 스타일 (패딩 - 좌우 10px로 여유 있게)
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(10, 4, 10, 4)));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, null));
            dataGrid.CellStyle = cellStyle;

            return dataGrid;
        }

        /// <summary>
        /// DataGrid TextColumn 생성 헬퍼
        /// </summary>
        private DataGridTextColumn CreateDataGridTextColumn(
            string header, 
            string bindingPath, 
            double width = double.NaN,
            HorizontalAlignment hAlign = HorizontalAlignment.Left,
            string? fontFamily = null,
            string? foregroundColor = null,
            FontWeight? fontWeight = null)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(bindingPath) { Mode = BindingMode.OneTime },
                Width = width > 0 ? new DataGridLength(width) : DataGridLength.Auto
            };

            // ElementStyle 설정
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, hAlign));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0)));  // 좌우 여백
            
            if (fontFamily != null)
                style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(fontFamily)));
            if (fontWeight != null)
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, fontWeight));
            if (foregroundColor != null)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(foregroundColor);
                    style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(color)));
                }
                catch { }
            }

            column.ElementStyle = style;
            return column;
        }

        /// <summary>
        /// 동적 DataGrid 컬럼 생성
        /// </summary>
        private DataGridTextColumn CreateDataGridDynamicColumn(FieldConfig config)
        {
            var headerText = config.DisplayName.Replace("_", "__");
            var binding = new Binding($"Fields[{config.FieldName}]") { Mode = BindingMode.OneTime };

            if (!string.IsNullOrEmpty(config.ValueMapping))
            {
                binding.Converter = new Converters.FieldValueConverter { Config = config };
            }

            var column = new DataGridTextColumn
            {
                Header = headerText,
                Binding = binding,
                Width = config.ColumnWidth > 0 ? new DataGridLength(config.ColumnWidth) : DataGridLength.Auto
            };

            // 기본 스타일 (좌우 여백)
            var baseStyle = new Style(typeof(TextBlock));
            baseStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0)));
            baseStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));

            // RETURN_CODE 특별 처리
            if (config.FieldName == "RETURN_CODE")
            {
                baseStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                
                var converter = TryFindResource("ReturnCodeColorConverter") as System.Windows.Data.IValueConverter;
                if (converter != null)
                {
                    var colorBinding = new Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = converter,
                        Mode = BindingMode.OneTime
                    };
                    baseStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, colorBinding));
                }
            }
            // ERROR_CODE 특별 처리
            else if (config.FieldName == "ERROR_CODE")
            {
                var converter = TryFindResource("ErrorColorConverter") as System.Windows.Data.IValueConverter;
                if (converter != null)
                {
                    var colorBinding = new Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = converter,
                        Mode = BindingMode.OneTime
                    };
                    baseStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, colorBinding));
                }
            }

            column.ElementStyle = baseStyle;
            return column;
        }

        /// <summary>
        /// 기본 컬럼 생성 헬퍼
        /// </summary>
        private GridViewColumn CreateColumn(string header, string bindingPath, double width, 
            string? fontFamily = null, FontWeight? fontWeight = null, 
            HorizontalAlignment hAlign = HorizontalAlignment.Left, bool trimming = false,
            string? foregroundColor = null)
        {
            var column = new GridViewColumn
            {
                Header = header,
                Width = width
            };

            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            
            // Binding Mode를 OneTime으로 설정 (읽기 전용 데이터)
            var binding = new Binding(bindingPath) { Mode = BindingMode.OneTime };
            factory.SetBinding(TextBlock.TextProperty, binding);
            
            // 컬럼 간격 확보를 위한 좌우 여백 (6px로 최적화)
            factory.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0));
            
            if (fontFamily != null)
                factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily(fontFamily));
            if (fontWeight != null)
                factory.SetValue(TextBlock.FontWeightProperty, fontWeight);
            if (hAlign != HorizontalAlignment.Left)
                factory.SetValue(TextBlock.HorizontalAlignmentProperty, hAlign);
            if (trimming)
            {
                factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                factory.SetBinding(FrameworkElement.ToolTipProperty, new Binding(bindingPath));
            }
            if (foregroundColor != null)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(foregroundColor);
                    factory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(color));
                    factory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
                }
                catch { }
            }

            template.VisualTree = factory;
            column.CellTemplate = template;

            return column;
        }

        /// <summary>
        /// 설정에 따라 동적 컬럼 생성
        /// </summary>
        private void InitializeDynamicColumns()
        {
            // 이 메서드는 이제 InitializeTabs에서 처리됨
        }


        /// <summary>
        /// 필드 설정에 따라 GridViewColumn 생성
        /// </summary>
        private GridViewColumn CreateDynamicColumn(FieldConfig config, ListView? listView = null)
        {
            // 헤더에서 언더바를 두 개로 변경 (WPF AccessKey 문제 해결)
            var headerText = config.DisplayName.Replace("_", "__");
            
            var column = new GridViewColumn
            {
                Header = headerText,
                // Width 0 이하면 Auto로 처리하되 최소 60px 보장
                Width = config.ColumnWidth > 0 ? config.ColumnWidth : double.NaN
            };
            
            // AUTO 모드일 때도 최소 너비 확보
            if (config.ColumnWidth <= 0)
            {
                // GridViewColumn은 MinWidth가 없으므로 HeaderTemplate으로 최소 너비 확보
                // 실제로는 Content의 Margin이 이 역할을 함
            }

            // DataTemplate 생성
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            
            // 컬럼 간격 확보를 위한 좌우 여백 (6px로 최적화)
            factory.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 6, 0));

            // Fields 딕셔너리에서 값 가져오는 바인딩 (OneTime으로 성능 최적화)
            var binding = new System.Windows.Data.Binding($"Fields[{config.FieldName}]")
            {
                Mode = BindingMode.OneTime
            };
            
            // ValueMapping이 있으면 Converter 적용
            if (!string.IsNullOrEmpty(config.ValueMapping))
            {
                binding.Converter = new Converters.FieldValueConverter { Config = config };
            }
            
            factory.SetBinding(TextBlock.TextProperty, binding);

            // RETURN_CODE는 특별 처리 (색상)
            if (config.FieldName == "RETURN_CODE")
            {
                factory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                var converter = TryFindResource("ReturnCodeColorConverter") as System.Windows.Data.IValueConverter;
                if (converter != null)
                {
                    var colorBinding = new System.Windows.Data.Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = converter
                    };
                    factory.SetBinding(TextBlock.ForegroundProperty, colorBinding);
                }
            }
            // ERROR_CODE도 특별 처리
            else if (config.FieldName == "ERROR_CODE")
            {
                factory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                var converter = TryFindResource("ErrorColorConverter") as System.Windows.Data.IValueConverter;
                if (converter != null)
                {
                    var colorBinding = new System.Windows.Data.Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = converter
                    };
                    factory.SetBinding(TextBlock.ForegroundProperty, colorBinding);
                }
            }

            template.VisualTree = factory;
            column.CellTemplate = template;

            return column;
        }

        private void InitializeLogManager()
        {
            logManager = new LogViewerManager();
            logEntries = logManager.LogEntries;
            displayEntries = new ObservableCollection<LogEntry>(logEntries);

            // 기존 단일 view는 더 이상 사용하지 않음 (탭별로 관리)
            logView = CollectionViewSource.GetDefaultView(displayEntries);
            logView.Filter = FilterLogEntry;

            // listViewLog는 이제 탭 안에 있으므로 여기서 설정하지 않음

            logEntries.CollectionChanged += LogEntries_CollectionChanged;
        }

        private void UpdateModeText()
        {
            string modeText = loadMode switch
            {
                LogLoadMode.NewOnly => "📍 실행 시점 이후 로그만 표시",
                LogLoadMode.Recent => $"📚 최근 {recentCount}개 로드",
                LogLoadMode.All => "📖 전체 로그 로드",
                _ => ""
            };

            txtMode.Text = modeText;
        }

        private async void LoadLogs()
        {
            // 파일 존재 확인
            if (!File.Exists(currentLogFile))
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 로그 파일 없음: {currentLogFile}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"📄 로그 파일: {Path.GetFileName(currentLogFile)}");

            switch (loadMode)
            {
                case LogLoadMode.NewOnly:
                    SetCurrentFilePosition();
                    break;

                case LogLoadMode.Recent:
                    await LoadRecentLogsAsync();
                    break;

                case LogLoadMode.All:
                    await LoadAllLogsAsync();
                    break;
            }
        }

        private void SetCurrentFilePosition()
        {
            try
            {
                var fileInfo = new FileInfo(currentLogFile);
                lastPosition = fileInfo.Length;

                System.Diagnostics.Debug.WriteLine($"📍 현재 위치: {lastPosition:N0} bytes (기존 로그 스킵)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 위치 설정 실패: {ex.Message}");
            }
        }

        private async Task LoadRecentLogsAsync()
        {
            try
            {
                // 프로그레스바 표시
                ShowLoadingOverlay(true);
                UpdateLoadingStatus("파일을 읽는 중...");
                
                // UI 렌더링 시간 확보
                await Task.Delay(50);

                // 백그라운드 스레드에서 파일 읽기
                string content = await Task.Run(() => File.ReadAllText(currentLogFile, Encoding.UTF8));
                
                UpdateLoadingStatus("로그 파싱 중...");
                
                // 백그라운드 스레드에서 파싱
                var entries = await Task.Run(() => ParseLogEntries(content));

                // 최근 N개만
                var recentEntries = entries.TakeLast(recentCount).ToList();

                System.Diagnostics.Debug.WriteLine($"📖 최근 {recentEntries.Count}개 로그 로드 중...");
                UpdateLoadingStatus($"최근 {recentEntries.Count}개 로그 추가 중...");

                // 일괄 추가로 UI 갱신 최소화
                logManager.AddLogEntries(recentEntries);

                lastPosition = new FileInfo(currentLogFile).Length;

                System.Diagnostics.Debug.WriteLine($"✅ 로드 완료: {logManager.LogEntries.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 최근 로그 로드 실패: {ex.Message}");
            }
            finally
            {
                ShowLoadingOverlay(false);
            }
        }

        private async Task LoadAllLogsAsync()
        {
            try
            {
                // 프로그레스바 표시
                ShowLoadingOverlay(true);
                UpdateLoadingStatus("파일을 읽는 중...");
                
                // UI 렌더링 시간 확보
                await Task.Delay(50);

                // 백그라운드 스레드에서 파일 읽기
                string content = await Task.Run(() => File.ReadAllText(currentLogFile, Encoding.UTF8));
                
                System.Diagnostics.Debug.WriteLine($"📄 파일 크기: {content.Length} 문자");
                
                UpdateLoadingStatus("로그 파싱 중...");
                
                // 백그라운드 스레드에서 파싱
                var entries = await Task.Run(() => ParseLogEntries(content));
                
                // 시간대 필터 적용
                var startTime = filterStartTime;
                var endTime = filterEndTime;
                
                var filteredEntries = entries.Where(e => 
                {
                    var logTime = e.Timestamp.TimeOfDay;
                    return logTime >= startTime && logTime <= endTime;
                }).ToList();

                System.Diagnostics.Debug.WriteLine($"📖 전체 {entries.Count}개 중 시간필터({startTime:hh\\:mm} ~ {endTime:hh\\:mm}) 적용: {filteredEntries.Count}개 로드");
                UpdateLoadingStatus($"{filteredEntries.Count}개 로그 추가 중... (시간필터: {startTime:hh\\:mm}~{endTime:hh\\:mm})");

                // UI 스레드에서 추가
                logManager.AddLogEntries(filteredEntries);

                lastPosition = new FileInfo(currentLogFile).Length;

                System.Diagnostics.Debug.WriteLine($"✅ 로드 완료: {logManager.LogEntries.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 전체 로그 로드 실패: {ex.Message}");
                MessageBox.Show($"로그 로드 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 프로그레스바 숨김
                ShowLoadingOverlay(false);
                
                // 전체 로그 로드 완료 후 Auto Fit 자동 적용
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyAutoFit();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void StartFileWatcher()
        {
            try
            {
                // 절대 경로로 정규화
                currentLogFile = Path.GetFullPath(currentLogFile);
                logDirectory = Path.GetDirectoryName(currentLogFile) ?? "";
                string fileName = Path.GetFileName(currentLogFile);

                fileWatcher = new FileSystemWatcher(logDirectory, fileName);
                fileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                fileWatcher.InternalBufferSize = 65536;  // 버퍼 증가로 이벤트 손실 방지
                fileWatcher.Changed += FileWatcher_Changed;
                fileWatcher.Created += FileWatcher_Created;
                fileWatcher.EnableRaisingEvents = true;

                System.Diagnostics.Debug.WriteLine($"✅ 파일 감시 시작: {currentLogFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 파일 감시 시작 실패: {ex.Message}");
            }
        }

        private void FileWatcher_Created(object sender, FileSystemEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"📄 새 파일: {e.Name}");
            currentLogFile = e.FullPath;
            lastPosition = 0;
        }

        private void FileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🔔 FileWatcher 이벤트: {e.ChangeType} - {e.FullPath}");

            // 현재 로드된 파일만 감시 (정규화된 경로로 비교)
            string eventPath = Path.GetFullPath(e.FullPath);
            if (!string.Equals(eventPath, currentLogFile, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"⏭️ 다른 파일 무시: {eventPath} != {currentLogFile}");
                return;
            }

            // 디바운싱: 100ms 내 중복 이벤트 무시
            debounceTimer?.Dispose();
            debounceTimer = new System.Threading.Timer(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    ReadNewLogs(currentLogFile);
                });
            }, null, 100, System.Threading.Timeout.Infinite);
        }

        private void ReadNewLogs(string filePath)
        {
            // 이미 읽는 중이면 스킵
            lock (fileLock)
            {
                if (isReading) return;
                isReading = true;
            }

            try
            {
                // 파일이 쓰기 완료될 때까지 잠시 대기
                System.Threading.Thread.Sleep(50);

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fileStream.Length < lastPosition)
                    {
                        // 파일이 초기화된 경우
                        lastPosition = 0;
                    }

                    fileStream.Seek(lastPosition, SeekOrigin.Begin);

                    using (var reader = new StreamReader(fileStream, Encoding.UTF8, true, 4096, leaveOpen: true))
                    {
                        string newContent = reader.ReadToEnd();
                        
                        if (!string.IsNullOrEmpty(newContent))
                        {
                            // 버퍼에 이전 내용이 있으면 합쳐서 파싱
                            string contentToParse = multiLineBuffer.ToString() + newContent;
                            multiLineBuffer.Clear();

                            var entries = ParseLogEntries(contentToParse, out string remainingContent);
                            
                            // 완료되지 않은 마지막 엔트리는 버퍼에 보관
                            if (!string.IsNullOrEmpty(remainingContent))
                            {
                                multiLineBuffer.Append(remainingContent);
                            }

                            if (entries.Count > 0)
                            {
                                foreach (var entry in entries)
                                {
                                    logManager.AddLogEntry(entry);
                                }
                                System.Diagnostics.Debug.WriteLine($"📥 새 로그 {entries.Count}개");
                            }
                        }

                        lastPosition = fileStream.Position;
                    }
                }
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 파일 접근 대기 중: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 로그 읽기 실패: {ex.Message}");
            }
            finally
            {
                lock (fileLock)
                {
                    isReading = false;
                }
            }
        }

        /// <summary>
        /// 로그 내용을 파싱하여 LogEntry 리스트 반환
        /// </summary>
        private List<LogEntry> ParseLogEntries(string content, out string remainingContent)
        {
            var entries = new List<LogEntry>();
            remainingContent = "";

            if (string.IsNullOrWhiteSpace(content))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 파일 내용이 비어있음");
                return entries;
            }

            System.Diagnostics.Debug.WriteLine($"🔍 파싱 시작: 내용 길이 = {content.Length}");

            // 각 로그 엔트리 시작 위치 찾기
            var matches = LogStartPattern.Matches(content);
            
            System.Diagnostics.Debug.WriteLine($"🔍 정규식 매칭 결과: {matches.Count}개 발견");
            
            // 매칭 안되면 첫 100자 출력
            if (matches.Count == 0 && content.Length > 0)
            {
                var sample = content.Substring(0, Math.Min(200, content.Length));
                System.Diagnostics.Debug.WriteLine($"⚠️ 매칭 실패! 샘플:\n{sample}");
            }
            
            for (int i = 0; i < matches.Count; i++)
            {
                int startIndex = matches[i].Index;
                int endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
                
                // 마지막 엔트리이고 완전하지 않으면 버퍼에 보관 (실시간 감시용)
                // 초기 로드 시에는 remainingContent가 무시되므로 상관없음
                if (i == matches.Count - 1 && remainingContent != null)
                {
                    string lastEntry = content.Substring(startIndex);
                    string trimmed = lastEntry.TrimEnd();
                    
                    // 완료 조건: } 또는 : 로 끝나면 완료된 것으로 판단
                    bool isComplete = trimmed.EndsWith("}") || 
                                      trimmed.EndsWith(":") || 
                                      trimmed.EndsWith(": ");
                    
                    if (!isComplete)
                    {
                        remainingContent = lastEntry;
                        continue;
                    }
                }

                string entryText = content.Substring(startIndex, endIndex - startIndex);
                var entry = ParseSingleEntry(entryText);
                
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// 로그 내용을 파싱하여 LogEntry 리스트 반환 (초기 로드용)
        /// </summary>
        private List<LogEntry> ParseLogEntries(string content)
        {
            return ParseLogEntries(content, out _);
        }

        /// <summary>
        /// 단일 로그 엔트리 파싱
        /// 형식: [MM-DD-YYYY HH:mm:ss.fff][SENDDATA|RECV] DYNAMIC.EVENT.xxx={...}
        /// </summary>
        private LogEntry? ParseSingleEntry(string entryText)
        {
            try
            {
                // 첫 줄에서 타임스탬프와 방향 추출
                var headerMatch = LogStartPattern.Match(entryText);
                if (!headerMatch.Success)
                    return null;

                string timestampStr = headerMatch.Groups[1].Value;
                // [TYPE] 형식 - RECV, SENDDATA 등
                string direction = headerMatch.Groups[2].Value;

                // 타임스탬프 파싱 (밀리초 있는 경우와 없는 경우 모두 처리)
                DateTime timestamp;
                string[] formats = { "MM-dd-yyyy HH:mm:ss.fff", "MM-dd-yyyy HH:mm:ss" };
                if (!DateTime.TryParseExact(timestampStr, formats, 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out timestamp))
                {
                    timestamp = DateTime.Now;
                }

                // ELEMENT 섹션에서 MSGID 추출
                string msgId = "";
                var msgIdMatch = Regex.Match(entryText, @"<MSGID=([^>]*)>");
                if (msgIdMatch.Success)
                {
                    msgId = msgIdMatch.Groups[1].Value;
                }

                // PROCID 추출
                string procId = "";
                var procIdMatch = Regex.Match(entryText, @"<PROCID=([^>]*)>");
                if (procIdMatch.Success)
                {
                    procId = procIdMatch.Groups[1].Value;
                }

                // ITEM 섹션에서 NAME/VALUE 쌍들 추출
                var fields = new Dictionary<string, string>();
                
                // PROCID 추가
                if (!string.IsNullOrEmpty(procId))
                {
                    fields["PROCID"] = procId;
                }

                // 모든 NAME/VALUE 쌍 추출
                var itemMatches = Regex.Matches(entryText, @"<NAME=([^>]*)>\s*<VALUE=([^>]*)>", RegexOptions.Singleline);
                foreach (Match match in itemMatches)
                {
                    string name = match.Groups[1].Value.Trim();
                    string value = match.Groups[2].Value.Trim();
                    fields[name] = value;
                }

                // 발견된 필드명을 LogFieldAnalyzer에 등록
                LogFieldAnalyzer.AddDiscoveredFields(fields.Keys);

                // 방향 표시 변환
                string displayDirection = direction.ToUpperInvariant() switch
                {
                    "SENDDATA" => "SEND",
                    "SEND" => "SEND",
                    "RECV" => "RECV",
                    "RECVDATA" => "RECV",
                    "LGEKC" => "SEND",  // 이벤트 데이터는 SEND로 표시
                    "SYSTEM" => "RECV", // 시스템 로그는 RECV로 표시
                    _ => "RECV"
                };

                // 의미 없는 로그 필터링: MsgId가 없고 필드도 거의 없는 경우 무시
                // (최소 3개 이상의 필드가 있거나 MsgId가 있어야 유효한 로그로 판단)
                bool hasMsgId = !string.IsNullOrWhiteSpace(msgId);
                bool hasEnoughFields = fields.Count >= 3;
                bool hasImportantFields = fields.ContainsKey("WORK_TYPE") || 
                                         fields.ContainsKey("LOTID") || 
                                         fields.ContainsKey("POSITION");

                if (!hasMsgId && !hasEnoughFields && !hasImportantFields)
                {
                    // 불완전한 로그는 스킵 (Debug.WriteLine 제거하여 성능 개선)
                    return null;
                }

                return new LogEntry
                {
                    Timestamp = timestamp,
                    Direction = displayDirection,
                    MessageId = msgId,
                    WorkType = fields.GetValueOrDefault("WORK_TYPE", ""),
                    ReturnCode = fields.GetValueOrDefault("RETURN_CODE", ""),
                    ErrorCode = fields.GetValueOrDefault("ERROR_CODE", ""),
                    RawData = entryText.Trim(),
                    Fields = fields
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 파싱 실패: {ex.Message}");
                return null;
            }
        }

        private void LogEntries_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (isPaused)
            {
                if (e.NewItems != null)
                {
                    foreach (LogEntry item in e.NewItems)
                    {
                        pausedBuffer.Add(item);
                    }

                    UpdateStatus();
                }
            }
            else
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    if (e.NewItems != null)
                    {
                        foreach (LogEntry item in e.NewItems)
                        {
                            // ROW 번호 할당 (전체 로그의 순번)
                            item.RowNumber = displayEntries.Count + 1;
                            
                            displayEntries.Add(item);
                            
                            // 매칭된 첫 번째 탭 이름 찾기 (통합 탭 제외)
                            string? matchedTabName = null;
                            
                            // 각 탭에 로그 추가 (조건 체크는 여기서 한 번만)
                            foreach (var kvp in tabDisplayEntries)
                            {
                                var tabConfig = kvp.Key;
                                var entries = kvp.Value;
                                
                                // 탭의 조건에 맞는 경우에만 추가
                                if (tabConfig.IsMatch(item))
                                {
                                    entries.Add(item);
                                    
                                    // 첫 번째 매칭된 비통합 탭 이름 저장
                                    if (matchedTabName == null && !tabConfig.IsIntegrated)
                                    {
                                        matchedTabName = tabConfig.Name;
                                    }
                                }
                            }
                            
                            // 매칭된 탭 이름 설정
                            item.MatchedTabName = matchedTabName ?? "";
                        }
                    }
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    displayEntries.Clear();
                    
                    // 모든 탭의 데이터도 클리어
                    foreach (var entries in tabDisplayEntries.Values)
                    {
                        entries.Clear();
                    }
                }

                // View.Refresh()는 검색 필터 변경 시에만 호출 (여기서는 호출 안 함)
                UpdateTabCountsThrottled();
                AutoScrollToBottom();
            }
        }

        // 디바운싱용 타이머
        private System.Threading.Timer? statusUpdateTimer;
        private readonly object statusLock = new object();

        /// <summary>
        /// 탭 카운트 업데이트 (디바운싱 적용)
        /// </summary>
        private void UpdateTabCountsThrottled()
        {
            lock (statusLock)
            {
                statusUpdateTimer?.Dispose();
                statusUpdateTimer = new System.Threading.Timer(_ =>
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        UpdateTabCounts();
                        UpdateStatus();
                    });
                }, null, 200, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>
        /// 모든 탭의 View 새로고침
        /// </summary>
        private void RefreshAllTabViews()
        {
            // 초기화 중에는 tabViews가 비어있을 수 있음
            if (tabViews == null || tabViews.Count == 0) return;
            
            // 필터가 있으면 각 뷰에 필터를 재설정 (초기화 시점에 필터가 없었을 경우 대비)
            bool hasFilter = !string.IsNullOrWhiteSpace(txtSearch?.Text) || 
                             chkSendOnly?.IsChecked == true || 
                             chkRecvOnly?.IsChecked == true ||
                             cboResultFilter?.SelectedIndex > 0;

            foreach (var kvp in tabViews)
            {
                var tabConfig = kvp.Key;
                var view = kvp.Value;
                if (view == null) continue;

                if (hasFilter)
                {
                    view.Filter = item => FilterLogEntry(item, tabConfig);
                }
                else
                {
                    view.Filter = null;
                }
                view.Refresh();
            }
            UpdateTabCounts();
        }

        /// <summary>
        /// 탭 헤더의 카운트 업데이트
        /// </summary>
        private void UpdateTabCounts()
        {
            // 초기화 중에는 tabControlLogs가 null일 수 있음
            if (tabControlLogs == null) return;
            
            foreach (TabItem tabItem in tabControlLogs.Items)
            {
                if (tabItem.Tag is TabConfig tabConfig && 
                    tabItem.Header is StackPanel headerPanel &&
                    headerPanel.Children.Count > 1 &&
                    headerPanel.Children[1] is TextBlock countText)
                {
                    if (tabDisplayEntries.TryGetValue(tabConfig, out var entries))
                    {
                        countText.Text = $"({entries.Count:N0})";
                    }
                }
            }
        }

        /// <summary>
        /// 탭 선택 변경 처리
        /// </summary>
        private void TabControlLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabControlLogs.SelectedItem is TabItem tabItem && tabItem.Tag is TabConfig tabConfig)
            {
                currentTabConfig = tabConfig;
                
                if (tabDataGrids.TryGetValue(tabConfig, out var dataGrid))
                {
                    currentDataGrid = dataGrid;
                }

                if (tabViews.TryGetValue(tabConfig, out var view))
                {
                    logView = view;
                }

                // 탭 인덱스는 메모리에만 저장 (창 닫을 때 저장)
                lastSelectedTabIndex = tabControlLogs.SelectedIndex;

                UpdateStatus();
                
                // 탭 전환 시 AutoFit 자동 실행
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyAutoFitForCurrentTab();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// 현재 탭에만 AutoFit 적용
        /// </summary>
        private void ApplyAutoFitForCurrentTab()
        {
            try
            {
                if (currentDataGrid != null)
                {
                    foreach (var column in currentDataGrid.Columns)
                    {
                        column.Width = DataGridLength.Auto;
                    }
                    
                    currentDataGrid.UpdateLayout();
                    
                    foreach (var column in currentDataGrid.Columns)
                    {
                        double actualWidth = column.ActualWidth;
                        if (actualWidth > 0)
                        {
                            // 여유 공간 추가 (15px)
                            column.Width = new DataGridLength(actualWidth + 15);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Auto Fit 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// DataGrid 더블클릭 - 로그 상세 보기 팝업
        /// </summary>
        private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.SelectedItem is LogEntry entry)
            {
                var popup = new LogDetailPopup(entry);
                popup.Owner = this;
                popup.ShowDialog();
            }
        }

        /// <summary>
        /// DataGrid 키보드 이벤트 - Ctrl+C로 선택된 셀 값 복사
        /// </summary>
        private void DataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.C && 
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
            {
                if (sender is DataGrid dataGrid && dataGrid.CurrentCell.Column != null && dataGrid.SelectedItem is LogEntry entry)
                {
                    // 현재 셀의 값 가져오기
                    var column = dataGrid.CurrentCell.Column;
                    string? cellValue = null;

                    if (column.Header is string header)
                    {
                        // 바인딩 경로에서 값 추출
                        cellValue = header switch
                        {
                            "#" => entry.RowNumber.ToString(),
                            "시간" => entry.TimeString,
                            "구분" => entry.DirectionText,
                            "MsgId" => entry.MessageId,
                            "분류" => entry.MatchedTabName,
                            "주요내용" => entry.Summary,
                            _ => null
                        };

                        // 동적 필드에서 찾기
                        if (cellValue == null)
                        {
                            var fieldName = header.Replace("__", "_"); // WPF AccessKey 복원
                            if (entry.Fields.TryGetValue(fieldName, out var fieldValue))
                            {
                                cellValue = fieldValue;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        try
                        {
                            Clipboard.SetText(cellValue);
                            e.Handled = true; // 기본 복사 동작 방지
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// 자동 스크롤 토글 버튼 클릭
        /// </summary>
        private void BtnAutoScroll_Click(object sender, RoutedEventArgs e)
        {
            isAutoScrollEnabled = !isAutoScrollEnabled;
            
            if (sender is Button btn)
            {
                if (isAutoScrollEnabled)
                {
                    btn.Content = "⬇ 자동스크롤";
                    btn.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                }
                else
                {
                    btn.Content = "⬇ 자동스크롤 OFF";
                    btn.Background = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
                }
            }
        }



        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            isPaused = !isPaused;

            if (isPaused)
            {
                btnPause.Content = "▶ 재개";
                btnPause.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0));

                pausedBuffer.Clear();

                System.Diagnostics.Debug.WriteLine("🔴 일시정지");
            }
            else
            {
                btnPause.Content = "⏸ 일시정지";
                btnPause.Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));

                if (pausedBuffer.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🟢 버퍼 {pausedBuffer.Count}개 추가");

                    foreach (var entry in pausedBuffer)
                    {
                        displayEntries.Add(entry);
                        
                        // 탭별 컬렉션에도 추가
                        string? matchedTabName = null;
                        foreach (var kvp in tabDisplayEntries)
                        {
                            var tabConfig = kvp.Key;
                            var entries = kvp.Value;
                            
                            if (tabConfig.IsMatch(entry))
                            {
                                entries.Add(entry);
                                
                                if (matchedTabName == null && !tabConfig.IsIntegrated)
                                {
                                    matchedTabName = tabConfig.Name;
                                }
                            }
                        }
                        
                        entry.MatchedTabName = matchedTabName ?? "";
                    }

                    pausedBuffer.Clear();
                    logView?.Refresh();
                    AutoScrollToBottom();
                }

                UpdateStatus();
                System.Diagnostics.Debug.WriteLine("🟢 재개");
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            ExportToExcel();
        }

        private void AutoScrollToBottom()
        {
            // 자동 스크롤이 비활성화되어 있으면 스킵
            if (!isAutoScrollEnabled)
                return;
                
            var dataGrid = currentDataGrid;
            if (dataGrid != null && dataGrid.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        dataGrid.ScrollIntoView(dataGrid.Items[dataGrid.Items.Count - 1]);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateStatus()
        {
            // 초기화 중에는 컬렉션이 null일 수 있음
            if (displayEntries == null || txtStatus == null) return;
            
            int displayCount = 0;
            int filteredCount = 0;

            // 현재 탭의 카운트 표시
            if (currentTabConfig != null && tabDisplayEntries != null && tabDisplayEntries.TryGetValue(currentTabConfig, out var entries))
            {
                displayCount = entries.Count;
                filteredCount = displayCount;  // 기본값
                
                // 필터링된 카운트는 검색 필터가 있을 때만 표시 (계산은 비동기로)
                if (!string.IsNullOrWhiteSpace(txtSearch?.Text) || 
                    chkSendOnly?.IsChecked == true || 
                    chkRecvOnly?.IsChecked == true ||
                    cboResultFilter?.SelectedIndex > 0)
                {
                    // 필터가 있을 때는 "(필터 적용중)" 표시
                    // 실제 카운트는 무거우므로 표시하지 않음
                    txtStatus.Text = $"[{currentTabConfig?.Name ?? "전체"}] 로그: {displayCount} (필터 적용중)";
                    return;
                }
            }
            else
            {
                displayCount = displayEntries.Count;
                filteredCount = displayCount;
            }

            string tabName = currentTabConfig?.Name ?? "전체";

            if (isPaused && pausedBuffer.Count > 0)
            {
                txtStatus.Text = $"[{tabName}] 로그: {displayCount} (⏸ 대기: {pausedBuffer.Count})";
            }
            else if (isPaused)
            {
                txtStatus.Text = $"[{tabName}] 로그: {displayCount} (⏸ 일시정지)";
            }
            else if (displayCount != filteredCount)
            {
                txtStatus.Text = $"[{tabName}] 로그: {displayCount} (필터: {filteredCount})";
            }
            else
            {
                txtStatus.Text = $"[{tabName}] 로그: {displayCount}";
            }
        }

        private bool FilterLogEntry(object item)
        {
            return FilterLogEntry(item, null);
        }

        private bool FilterLogEntry(object item, TabConfig? tabConfig)
        {
            if (!(item is LogEntry entry))
                return false;

            // 탭 필터 (탭 자체 조건은 데이터 추가 시 이미 적용됨)
            // 여기서는 검색 필터만 적용

            string searchText = txtSearch?.Text ?? "";
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string[] orGroups = searchText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                bool anyGroupMatch = false;

                foreach (var orGroup in orGroups)
                {
                    string group = orGroup.Trim();
                    if (string.IsNullOrEmpty(group))
                        continue;

                    if (group.Contains("+"))
                    {
                        string[] andKeywords = group.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);

                        bool allMatch = true;
                        foreach (var keyword in andKeywords)
                        {
                            string kw = keyword.Trim().ToLower();
                            if (string.IsNullOrEmpty(kw))
                                continue;

                            if (!CheckKeywordMatch(entry, kw))
                            {
                                allMatch = false;
                                break;
                            }
                        }

                        if (allMatch)
                        {
                            anyGroupMatch = true;
                            break;
                        }
                    }
                    else
                    {
                        string kw = group.ToLower();
                        if (CheckKeywordMatch(entry, kw))
                        {
                            anyGroupMatch = true;
                            break;
                        }
                    }
                }

                if (!anyGroupMatch)
                    return false;
            }

            if (chkSendOnly?.IsChecked == true && entry.Direction != "SEND")
                return false;

            if (chkRecvOnly?.IsChecked == true && entry.Direction != "RECV")
                return false;

            // OK/NG 필터
            if (cboResultFilter?.SelectedIndex > 0)
            {
                var selectedItem = cboResultFilter.SelectedItem as ComboBoxItem;
                string? content = selectedItem?.Content?.ToString();
                
                if (content?.Contains("OK") == true)
                {
                    // OK만 표시
                    if (entry.ReturnCode.ToUpperInvariant() != "OK")
                        return false;
                }
                else if (content?.Contains("NG") == true)
                {
                    // NG만 표시 (OK가 아닌 모든 것)
                    if (string.IsNullOrEmpty(entry.ReturnCode) || entry.ReturnCode.ToUpperInvariant() == "OK")
                        return false;
                }
            }

            return true;
        }

        private bool CheckKeywordMatch(LogEntry entry, string keyword)
        {
            return entry.MessageId.ToLower().Contains(keyword) ||
                   entry.Summary.ToLower().Contains(keyword) ||
                   entry.DirectionText.ToLower().Contains(keyword) ||
                   entry.TimeString.Contains(keyword) ||
                   entry.ReturnCode.ToLower().Contains(keyword) ||
                   entry.WorkType.ToLower().Contains(keyword) ||
                   entry.LotId.ToLower().Contains(keyword) ||
                   entry.ErrorCode.ToLower().Contains(keyword) ||
                   entry.PalletId.ToLower().Contains(keyword) ||
                   entry.Fields.Any(f =>
                       f.Key.ToLower().Contains(keyword) ||
                       f.Value.ToLower().Contains(keyword));
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "모든 로그를 삭제하시겠습니까?",
                "확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                displayEntries.Clear();
                pausedBuffer.Clear();
                
                // 모든 탭의 데이터도 클리어
                foreach (var entries in tabDisplayEntries.Values)
                {
                    entries.Clear();
                }
                
                RefreshAllTabViews();
                UpdateStatus();
            }
        }

        private void BtnColumnSettings_Click(object sender, RoutedEventArgs e)
        {
            // 현재 선택된 프리셋 이름 전달
            var currentPreset = cboPresets.SelectedItem?.ToString();
            var settingsWindow = new ColumnSettingsWindow(currentLogFile, currentPreset);
            settingsWindow.Owner = this;
            if (settingsWindow.ShowDialog() == true)
            {
                // 프리셋 목록 새로고침
                LoadPresetList();
                
                // 설정이 변경되면 탭 재초기화
                InitializeTabs();
                ReloadExistingLogs();
                
                // 폰트 크기 적용
                LoadSavedFontSize();
            }
        }

        /// <summary>
        /// 기존 로그를 재로드하여 탭에 분배
        /// </summary>
        private void ReloadExistingLogs()
        {
            // null 체크
            if (logEntries == null || displayEntries == null || tabDisplayEntries == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ ReloadExistingLogs: 컬렉션이 초기화되지 않음");
                return;
            }

            var existingEntries = logEntries.ToList();
            displayEntries.Clear();
            
            foreach (var entries in tabDisplayEntries.Values)
            {
                entries.Clear();
            }

            foreach (var entry in existingEntries)
            {
                displayEntries.Add(entry);
                
                foreach (var kvp in tabDisplayEntries)
                {
                    var tabConfig = kvp.Key;
                    var tabEntries = kvp.Value;

                    if (tabConfig.IsMatch(entry))
                    {
                        tabEntries.Add(entry);
                    }
                }
            }

            RefreshAllTabViews();
            UpdateTabCounts();
            UpdateStatus();
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RefreshAllTabViews();
                UpdateStatus();
                e.Handled = true;
            }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            RefreshAllTabViews();
            UpdateStatus();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshAllTabViews();
            UpdateStatus();
        }

        /// <summary>
        /// OK/NG 필터 변경
        /// </summary>
        private void CboResultFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 초기화 중에는 무시
            if (cboResultFilter == null) return;
            
            RefreshAllTabViews();
            UpdateStatus();
        }

        private void BtnFontMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int size) && size > 8)
            {
                txtFontSize.Text = (size - 1).ToString();
                ApplyFontSize(size - 1);
            }
        }

        private void BtnFontPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int size) && size < 20)
            {
                txtFontSize.Text = (size + 1).ToString();
                ApplyFontSize(size + 1);
            }
        }

        private void ApplyFontSize(int size)
        {
            // 모든 탭의 DataGrid에 폰트 사이즈 적용
            foreach (var dataGrid in tabDataGrids.Values)
            {
                dataGrid.FontSize = size;
            }
            
            // 설정에 저장
            SaveFontSize(size);
        }

        private void LoadSavedFontSize()
        {
            var settings = ColumnSettingsManager.CurrentSettings;
            int fontSize = settings.FontSize > 0 ? settings.FontSize : 11;
            txtFontSize.Text = fontSize.ToString();
            ApplyFontSizeWithoutSave(fontSize);
        }

        private void ApplyFontSizeWithoutSave(int size)
        {
            foreach (var dataGrid in tabDataGrids.Values)
            {
                dataGrid.FontSize = size;
            }
        }

        private void SaveFontSize(int size)
        {
            var settings = ColumnSettingsManager.CurrentSettings;
            settings.FontSize = size;
            ColumnSettingsManager.SaveCurrentSettings(settings);
        }

        /// <summary>
        /// Auto Fit 버튼 클릭 - 모든 컬럼을 컨텐츠에 맞게 자동 조정
        /// </summary>
        private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoFit();
        }

        /// <summary>
        /// 모든 컬럼을 컨텐츠에 맞게 자동 조정
        /// </summary>
        private void ApplyAutoFit()
        {
            try
            {
                // 모든 탭의 DataGrid에 접근하여 컬럼 너비 자동 조정
                foreach (var dataGrid in tabDataGrids.Values)
                {
                    foreach (var column in dataGrid.Columns)
                    {
                        // AUTO 모드로 설정하여 최적 너비 계산
                        column.Width = DataGridLength.Auto;
                    }
                    
                    // UI 업데이트를 기다림
                    dataGrid.UpdateLayout();
                    
                    // 계산된 너비를 고정값으로 변환 (여유 공간 15px 추가)
                    foreach (var column in dataGrid.Columns)
                    {
                        double actualWidth = column.ActualWidth;
                        if (actualWidth > 0)
                        {
                            column.Width = new DataGridLength(actualWidth + 15);
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine("✅ Auto Fit 적용: 모든 컬럼 너비를 최적화 후 고정 (+15px 여유)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Auto Fit 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 탭 다시 로드
        /// </summary>
        private void ReloadTabs()
        {
            // 현재 데이터 백업
            var currentEntries = displayEntries.ToList();
            
            // 탭 다시 생성
            InitializeTabs();
            
            // 데이터 다시 분배
            foreach (var entry in currentEntries)
            {
                foreach (var kvp in tabDisplayEntries)
                {
                    var tabConfig = kvp.Key;
                    var entries = kvp.Value;
                    
                    if (tabConfig.IsMatch(entry))
                    {
                        entries.Add(entry);
                    }
                }
            }
            
            RefreshAllTabViews();
            UpdateStatus();
        }


        protected override void OnClosing(CancelEventArgs e)
        {
            // 마지막 선택한 탭 인덱스 저장
            var settings = ColumnSettingsManager.CurrentSettings;
            if (settings.TabSettings != null)
            {
                settings.TabSettings.LastSelectedTabIndex = lastSelectedTabIndex;
            }
            
            // 현재 설정 저장
            ColumnSettingsManager.SaveCurrentSettings(settings);
            
            fileWatcher?.Dispose();
            base.OnClosing(e);
        }

        /// <summary>
        /// Window가 로드될 때 자동으로 컬럼 FIT 적용
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Window_Loaded는 데이터 로드 전이므로 여기서는 Auto Fit 적용 안 함
            // LoadLogViewerWithSettings에서 처리됨
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // OnClosing에서 처리됨
        }

        /// <summary>
        /// 로딩 오버레이 표시/숨김
        /// </summary>
        private void ShowLoadingOverlay(bool show)
        {
            Dispatcher.Invoke(() =>
            {
                loadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        /// <summary>
        /// 로딩 상태 텍스트 업데이트
        /// </summary>
        private void UpdateLoadingStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLoadingStatus.Text = message;
            });
        }

        /// <summary>
        /// 현재 탭의 로그를 엑셀로 저장
        /// </summary>
        private void ExportToExcel()
        {
            try
            {
                // EPPlus 라이선스 설정
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                // 현재 탭의 데이터 가져오기
                var entries = GetCurrentTabEntries();
                if (entries == null || entries.Count == 0)
                {
                    MessageBox.Show("내보낼 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 저장 경로 선택
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                    FileName = $"로그_{selectedDate:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() != true)
                    return;

                // 프로그레스바 표시
                ShowLoadingOverlay(true);
                UpdateLoadingStatus("엑셀 파일 생성 중...");

                // 백그라운드 스레드에서 엑셀 생성
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (var package = new ExcelPackage())
                        {
                            var worksheet = package.Workbook.Worksheets.Add("로그");

                            // 헤더 생성
                            var headers = new List<string> { "#", "시간", "구분", "MsgId" };
                            
                            // 탭 이름 (통합 로그일 때만)
                            if (currentTabConfig?.IsIntegrated == true)
                            {
                                headers.Add("분류");
                            }

                            // 동적 컬럼 헤더
                            var settings = ColumnSettingsManager.CurrentSettings;
                            foreach (var field in settings.ColumnFields)
                            {
                                headers.Add(field.DisplayName);
                            }
                            
                            headers.Add("주요내용");

                            // 헤더 작성
                            for (int i = 0; i < headers.Count; i++)
                            {
                                worksheet.Cells[1, i + 1].Value = headers[i];
                                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                                worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                            }

                            // 데이터 작성
                            int row = 2;
                            foreach (var entry in entries)
                            {
                                int col = 1;
                                worksheet.Cells[row, col++].Value = entry.RowNumber;
                                worksheet.Cells[row, col++].Value = entry.TimeString;
                                worksheet.Cells[row, col++].Value = entry.DirectionText;
                                worksheet.Cells[row, col++].Value = entry.MessageId;

                                if (currentTabConfig?.IsIntegrated == true)
                                {
                                    worksheet.Cells[row, col++].Value = entry.MatchedTabName;
                                }

                                // 동적 필드
                                foreach (var field in settings.ColumnFields)
                                {
                                    var value = entry.Fields.GetValueOrDefault(field.FieldName, "");
                                    worksheet.Cells[row, col++].Value = value;
                                }

                                worksheet.Cells[row, col++].Value = entry.Summary;

                                // 배경색 (송신/수신)
                                var bgColor = entry.Direction == "SEND" 
                                    ? System.Drawing.Color.FromArgb(230, 240, 255)
                                    : System.Drawing.Color.FromArgb(230, 255, 230);
                                
                                for (int c = 1; c <= headers.Count; c++)
                                {
                                    worksheet.Cells[row, c].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                    worksheet.Cells[row, c].Style.Fill.BackgroundColor.SetColor(bgColor);
                                }

                                row++;
                            }

                            // 자동 너비 조정
                            worksheet.Cells.AutoFitColumns();

                            // 파일 저장
                            package.SaveAs(new FileInfo(saveDialog.FileName));
                        }

                        Dispatcher.Invoke(() =>
                        {
                            ShowLoadingOverlay(false);
                            
                            // 파일을 열겠냐고 물어보기
                            var result = MessageBox.Show(
                                $"엑셀 파일이 저장되었습니다.\n\n{saveDialog.FileName}\n\n파일을 열겠습니까?",
                                "완료",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Information
                            );

                            // 예를 선택하면 파일 열기
                            if (result == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = saveDialog.FileName,
                                        UseShellExecute = true
                                    });
                                }
                                catch (Exception openEx)
                                {
                                    MessageBox.Show(
                                        $"파일을 열 수 없습니다:\n{openEx.Message}",
                                        "오류",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Warning
                                    );
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ShowLoadingOverlay(false);
                            MessageBox.Show(
                                $"엑셀 저장 중 오류가 발생했습니다:\n{ex.Message}",
                                "오류",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error
                            );
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                ShowLoadingOverlay(false);
                MessageBox.Show($"엑셀 내보내기 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 현재 탭의 로그 엔트리 가져오기 (필터 적용된)
        /// </summary>
        private List<LogEntry> GetCurrentTabEntries()
        {
            if (currentTabConfig != null && tabViews.TryGetValue(currentTabConfig, out var view))
            {
                return view.Cast<LogEntry>().ToList();
            }
            return displayEntries.ToList();
        }

        /// <summary>
        /// 시간대 콤보박스 선택 변경
        /// </summary>
        private void CboTimeRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 초기화 중에는 txtStartTime/txtEndTime이 null일 수 있음
            if (txtStartTime == null || txtEndTime == null) return;
            
            if (cboTimeRange.SelectedItem is ComboBoxItem selectedItem)
            {
                string? content = selectedItem.Content?.ToString();
                if (string.IsNullOrEmpty(content)) return;

                switch (content)
                {
                    case "종일":
                        txtStartTime.Text = "00:00";
                        txtEndTime.Text = "23:59";
                        break;
                    case "오전":
                        txtStartTime.Text = "08:00";
                        txtEndTime.Text = "12:59";
                        break;
                    case "오후":
                        txtStartTime.Text = "13:00";
                        txtEndTime.Text = "16:59";
                        break;
                    case "잔업":
                        txtStartTime.Text = "17:30";
                        txtEndTime.Text = "20:30";
                        break;
                    default:
                        // 시간대별 (07시, 08시, ... 23시)
                        if (content.EndsWith("시") && int.TryParse(content.Replace("시", ""), out int hour))
                        {
                            txtStartTime.Text = $"{hour:D2}:00";
                            txtEndTime.Text = $"{hour:D2}:59";
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 시간 이동 텍스트박스 Enter 키 처리
        /// </summary>
        private void TxtJumpTime_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                JumpToTime();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 시간 이동 버튼 클릭
        /// </summary>
        private void BtnJumpToTime_Click(object sender, RoutedEventArgs e)
        {
            JumpToTime();
        }

        /// <summary>
        /// 지정한 시간으로 이동
        /// </summary>
        private void JumpToTime()
        {
            if (currentDataGrid == null || currentTabConfig == null) return;
            if (!tabDisplayEntries.TryGetValue(currentTabConfig, out var entries) || entries.Count == 0) return;

            string timeText = txtJumpTime.Text.Trim();
            if (string.IsNullOrEmpty(timeText)) return;

            // 시간 파싱 (HH:mm 또는 HH:mm:ss 또는 HH 형식)
            TimeSpan targetTime;
            if (timeText.Length <= 2 && int.TryParse(timeText, out int hourOnly))
            {
                // "09" 또는 "9" 형식 -> 09:00:00
                targetTime = new TimeSpan(hourOnly, 0, 0);
            }
            else if (TimeSpan.TryParse(timeText, out var parsed))
            {
                targetTime = parsed;
            }
            else if (TimeSpan.TryParse(timeText + ":00", out var parsed2))
            {
                targetTime = parsed2;
            }
            else
            {
                MessageBox.Show("시간 형식이 올바르지 않습니다.\n예: 09:30, 14:00, 9", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 해당 시간 이후의 첫 번째 로그 찾기
            LogEntry? targetEntry = null;
            int targetIndex = -1;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Timestamp.TimeOfDay >= targetTime)
                {
                    targetEntry = entries[i];
                    targetIndex = i;
                    break;
                }
            }

            if (targetEntry == null)
            {
                // 해당 시간 이후 로그가 없으면 마지막 로그로 이동
                MessageBox.Show($"{targetTime:hh\\:mm} 이후의 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 자동 스크롤 임시 비활성화
                bool wasAutoScrollEnabled = isAutoScrollEnabled;
                isAutoScrollEnabled = false;

                // 해당 항목으로 스크롤 및 선택
                currentDataGrid.ScrollIntoView(targetEntry);
                currentDataGrid.SelectedItem = targetEntry;
                currentDataGrid.Focus();

                // 상태바에 이동 정보 표시
                txtStatus.Text = $"⏰ {targetTime:hh\\:mm} → {targetEntry.TimeString} (#{targetEntry.RowNumber})";

                // 자동 스크롤 복원
                isAutoScrollEnabled = wasAutoScrollEnabled;

                System.Diagnostics.Debug.WriteLine($"⏰ 시간 이동: {targetTime:hh\\:mm} → Row #{targetEntry.RowNumber} ({targetEntry.TimeString})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 시간 이동 실패: {ex.Message}");
            }
        }
    }
}
