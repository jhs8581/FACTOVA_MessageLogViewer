using FACTOVA_MessageLogViewer.Converters;
using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace FACTOVA_MessageLogViewer
{
    public partial class DataLogViewerControl : UserControl
    {
        private ObservableCollection<DataLogEntry> logEntries = new();
        private ObservableCollection<DataLogEntry> displayEntries = new();

        private FileSystemWatcher? fileWatcher;
        private string logDirectory = "";
        private string currentLogFile = "";
        private long lastPosition = 0;

        private bool isPaused = false;
        private List<DataLogEntry> pausedBuffer = new();

        private LogLoadMode loadMode;
        private int recentCount;

        private Dictionary<TabConfig, DataGrid> tabDataGrids = new();
        private Dictionary<TabConfig, System.ComponentModel.ICollectionView> tabViews = new();
        private Dictionary<TabConfig, ObservableCollection<DataLogEntry>> tabDisplayEntries = new();
        private DataGrid? currentDataGrid;
        private TabConfig? currentTabConfig;

        private StringBuilder multiLineBuffer = new StringBuilder();
        
        // DATA 로그 시작 패턴 (단순화 - 앵커 제거)
        private static readonly Regex DataLogStartPattern = new Regex(
            @"\[(\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)\]\s*ExecuteService\(\):\[\s*(\S+)\s*\]", 
            RegexOptions.Compiled);

        // 제외할 비즈명 목록 (하드코딩)
        private static readonly HashSet<string> ExcludedBizNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BR_SFC_REG_MAINFRAME_CONFIG",
            "DA_COM_SEL_MULTILANG_DATA",
            "BR_CUS_CHK_USER_AUTO_LOGIN",
            "DA_COM_SEL_SERVERTIME",
            "DA_CUS_SEL_SFC_PROGRAM_CONFIG_INFO",
            "DA_CUS_SEL_SFC_PROGRAM_MENU",
            "DA_CUS_SEL_FCTRY_AREA_CBO2",
            "BR_SFC_GetScanItemList"
        };

        private System.Threading.Timer? debounceTimer;
        private readonly object fileLock = new object();
        private bool isReading = false;

        private string currentLogDirectory = "";
        private bool isDefaultFolder = true;
        private bool isAutoScrollEnabled = true;
        private bool slowQueryOnly = false;
        private bool enableRealTimeWatch = true;

        private TimeSpan filterStartTime = TimeSpan.Zero;
        private TimeSpan filterEndTime = new TimeSpan(23, 59, 59);

        // 비즈 필터 관련
        private ObservableCollection<BizFilterItem> bizFilterItems = new();
        private HashSet<string> discoveredBizNames = new();
        private HashSet<string> selectedBizNames = new();

        public DataLogViewerControl()
        {
            InitializeComponent();
            cboBizFilter.ItemsSource = bizFilterItems;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        public void Cleanup()
        {
            StopFileWatcher();
            scrollDebounceTimer?.Dispose();
            statusDebounceTimer?.Dispose();
            debounceTimer?.Dispose();
            SaveConfig();
        }

        /// <summary>
        /// 설정으로 초기화 (MainWindow에서 호출)
        /// </summary>
        public void Initialize(LogViewerSettings settings)
        {
            // 기존 감시 중지
            StopFileWatcher();
            
            // 기존 데이터 클리어
            logEntries.Clear();
            displayEntries.Clear();
            foreach (var entries in tabDisplayEntries.Values)
            {
                entries.Clear();
            }

            currentLogFile = settings.LogFilePath;
            logDirectory = settings.LogDirectory;
            loadMode = settings.LoadMode;
            recentCount = settings.RecentCount;
            filterStartTime = settings.FilterStartTime;
            filterEndTime = settings.FilterEndTime;
            currentLogDirectory = settings.LogDirectory;
            isDefaultFolder = settings.IsDefaultFolder;
            slowQueryOnly = settings.SlowQueryOnly;
            enableRealTimeWatch = settings.EnableRealTimeWatch;

            if (string.IsNullOrEmpty(currentLogFile) || !File.Exists(currentLogFile))
            {
                System.Diagnostics.Debug.WriteLine($"📊 DATA 로그 파일 없음: {currentLogFile}");
                txtLogFolder.Text = "(파일 없음)";
                return;
            }

            txtLogFolder.Text = $"({Path.GetFileName(currentLogFile)})";

            InitializeLogViewer();
            LoadLogs();
            
            // 실시간 감지가 활성화된 경우에만 파일 감시 시작
            if (enableRealTimeWatch)
            {
                StartFileWatcher();
            }

            UpdateStatus();


            System.Diagnostics.Debug.WriteLine($"📊 DATA 로그 초기화 완료: {currentLogFile}");
        }

        #region 설정

        private void LoadConfig()
        {
            try
            {
                var settings = AppSettingsManager.Settings;
                
                // EventLogViewerControl과 동일한 폴더 사용
                if (!string.IsNullOrEmpty(settings.LastUsedFolder) && Directory.Exists(settings.LastUsedFolder))
                {
                    currentLogDirectory = settings.LastUsedFolder;
                    isDefaultFolder = false;
                }
                else if (!string.IsNullOrEmpty(settings.DefaultLogFolder) && Directory.Exists(settings.DefaultLogFolder))
                {
                    currentLogDirectory = settings.DefaultLogFolder;
                    isDefaultFolder = true;
                }

                if (settings.ColumnSettings?.FontSize > 0)
                    txtFontSize.Text = settings.ColumnSettings.FontSize.ToString();
            }
            catch { }
        }

        private void SaveConfig()
        {
            try
            {
                var settings = AppSettingsManager.Settings;
                settings.LastUsedFolder = currentLogDirectory;
                if (int.TryParse(txtFontSize.Text, out int fs) && settings.ColumnSettings != null)
                    settings.ColumnSettings.FontSize = fs;
                AppSettingsManager.SaveCurrent();
            }
            catch { }
        }

        #endregion

        #region 뷰어 시작

        private void InitializeLogViewer()
        {
            // 이전 이벤트 핸들러 제거 (중복 등록 방지)
            logEntries.CollectionChanged -= LogEntries_CollectionChanged;
            
            logEntries.Clear();
            displayEntries.Clear();
            tabDataGrids.Clear();
            tabViews.Clear();
            tabDisplayEntries.Clear();
            tabControl.Items.Clear();

            var settings = ColumnSettingsManager.CurrentSettings;
            var tabs = settings.DataTabSettings?.EnabledTabs?.ToList() ?? new List<TabConfig>();

            // 느린 쿼리 탭은 제외 (필터 영역에서 처리)
            tabs = tabs.Where(t => !t.Name.Contains("느린 쿼리")).ToList();

            if (tabs.Count == 0)
                tabs.Add(new TabConfig { Name = "통합 로그", IsIntegrated = true, IsEnabled = true });

            foreach (var tabConfig in tabs)
            {
                var tabEntries = new ObservableCollection<DataLogEntry>();
                tabDisplayEntries[tabConfig] = tabEntries;

                var dataGrid = CreateDataGrid(tabConfig);
                tabDataGrids[tabConfig] = dataGrid;

                var view = CollectionViewSource.GetDefaultView(tabEntries);
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
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)),  // 주황색
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                headerPanel.Children.Add(headerText);
                headerPanel.Children.Add(countText);

                tabControl.Items.Add(new TabItem { Header = headerPanel, Content = dataGrid, Tag = tabConfig });
                    }

                    // 탭이 1개만 있으면 탭 헤더 숨김
                    if (tabControl.Items.Count == 1)
                    {
                        tabControl.Template = CreateHiddenTabHeaderTemplate();
                    }

                    if (tabControl.Items.Count > 0)
                    {
                        tabControl.SelectedIndex = 0;
                
                        // 첫 번째 탭의 DataGrid와 TabConfig를 명시적으로 설정
                        if (tabControl.Items[0] is TabItem firstTab && firstTab.Tag is TabConfig firstCfg)
                        {
                            currentTabConfig = firstCfg;
                            currentDataGrid = tabDataGrids.GetValueOrDefault(firstCfg);
                        }
                    }

                    logEntries.CollectionChanged += LogEntries_CollectionChanged;
            
                    System.Diagnostics.Debug.WriteLine($"📊 InitializeLogViewer: 탭 {tabs.Count}개 초기화 완료");
                }

                /// <summary>
                /// 탭 헤더를 숨기는 템플릿 생성
                /// </summary>
                private ControlTemplate CreateHiddenTabHeaderTemplate()
                {
                    var template = new ControlTemplate(typeof(TabControl));
                    var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                    contentPresenter.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
                    template.VisualTree = contentPresenter;
                    return template;
                }

        private DataGrid CreateDataGrid(TabConfig tabConfig)
        {
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)),
                VerticalGridLinesBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230)),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                FontSize = int.TryParse(txtFontSize.Text, out int fs) ? fs : 11,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };

            // 가상화 설정 (휠 스크롤 최적화)
            VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
            VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetCacheLength(dataGrid, new VirtualizationCacheLength(10, 10));
            VirtualizingPanel.SetCacheLengthUnit(dataGrid, VirtualizationCacheLengthUnit.Item);
            VirtualizingPanel.SetScrollUnit(dataGrid, ScrollUnit.Item);

            // 스크롤 최적화
            ScrollViewer.SetIsDeferredScrollingEnabled(dataGrid, false);
            ScrollViewer.SetCanContentScroll(dataGrid, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(dataGrid, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(dataGrid, ScrollBarVisibility.Auto);

            // Row 스타일
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 28.0));
            dataGrid.RowStyle = rowStyle;

            // 헤더 스타일
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 13.0));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 8, 8, 8)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            dataGrid.ColumnHeaderStyle = headerStyle;

            // Cell 스타일
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(10, 4, 10, 4)));
            cellStyle.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, null));
            cellStyle.Setters.Add(new Setter(DataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            dataGrid.CellStyle = cellStyle;

            // 프리셋에서 컬럼 설정 가져오기
            var dataSettings = UnifiedPresetManager.CurrentPreset?.DataSettings;
            var columnFields = dataSettings?.EnabledFields?.ToList();

            if (columnFields != null && columnFields.Any())
            {
                // 프리셋 설정에서 컬럼 생성
                foreach (var field in columnFields)
                {
                    var column = CreateColumn(field);
                    if (column != null)
                        dataGrid.Columns.Add(column);
                }
            }
            else
            {
                // 기본 컬럼
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "No", Binding = new Binding("RowNumber"), Width = 50 });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "시간", Binding = new Binding("TimeString"), Width = 90 });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "비즈명", Binding = new Binding("BizName"), Width = 280 });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "실행시간", Binding = new Binding("ExecTime"), Width = 120 });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "TXN_ID", Binding = new Binding("TxnId"), Width = 180 });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "파라미터", Binding = new Binding("Summary"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            }

            dataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            dataGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;
            return dataGrid;
        }

        /// <summary>
        /// 필드 설정에서 DataGridColumn 생성
        /// </summary>
        private DataGridColumn? CreateColumn(DataFieldConfig field)
        {
            string bindingPath;
            
            if (field.IsParameter)
            {
                // 파라미터 필드: Fields 딕셔너리에서 가져오기
                bindingPath = $"Fields[{field.FieldName}]";
            }
            else
            {
                // 기본 필드: 직접 프로퍼티
                bindingPath = field.FieldName;
            }

            var binding = new Binding(bindingPath);
            
            // 값 변환 매핑이 있으면 컨버터 적용
            if (!string.IsNullOrEmpty(field.ValueMapping))
            {
                binding.Converter = new ValueMappingConverter(field.ValueMapping);
            }

            var width = field.ColumnWidth == 0 
                ? new DataGridLength(1, DataGridLengthUnitType.Star) 
                : new DataGridLength(field.ColumnWidth);

            var column = new DataGridTextColumn
            {
                Header = field.DisplayName,
                Binding = binding,
                Width = width
            };

            // ElementStyle 추가 - 세로 정렬 중앙
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 0, 5, 0)));
            column.ElementStyle = style;

            return column;
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is DataLogEntry entry)
            {
                var popup = new LogDetailPopup();
                popup.SetDataLogContent(entry);
                popup.Owner = Window.GetWindow(this);
                popup.ShowDialog();
            }
        }

        /// <summary>
        /// DataGrid 키보드 이벤트 - Ctrl+C로 선택된 셀 값 복사
        /// </summary>
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C &&
                (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (sender is DataGrid dataGrid && dataGrid.CurrentCell.Column != null && dataGrid.SelectedItem is DataLogEntry entry)
                {
                    var column = dataGrid.CurrentCell.Column;
                    string? cellValue = null;

                    if (column.Header is string header)
                    {
                        cellValue = header switch
                        {
                            "No" => entry.RowNumber.ToString(),
                            "시간" => entry.TimeString,
                            "비즈명" => entry.BizName,
                            "실행시간" => entry.ExecTime,
                            "TXN_ID" => entry.TxnId,
                            "파라미터" => entry.Summary,
                            _ => null
                        };

                        if (cellValue == null)
                        {
                            var fieldName = header.Replace("__", "_");
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
                            e.Handled = true;
                        }
                        catch { }
                    }
                }
            }
        }

        #endregion

        #region 로그 파싱

        private async void LoadLogs()
        {
            System.Diagnostics.Debug.WriteLine($"📂 LoadLogs 시작: {currentLogFile}");
            
            if (!File.Exists(currentLogFile))
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 파일 없음: {currentLogFile}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"📂 LoadMode: {loadMode}");

            switch (loadMode)
            {
                case LogLoadMode.NewOnly:
                    lastPosition = new FileInfo(currentLogFile).Length;
                    System.Diagnostics.Debug.WriteLine($"📍 NewOnly: position={lastPosition}");
                    break;
                case LogLoadMode.Recent:
                case LogLoadMode.All:
                    isLoadingBatch = true;  // 일괄 로드 시작
                    ShowLoadingOverlay(true);
                    UpdateLoadingStatus("파일을 읽는 중...");
                    
                    // 백그라운드에서 파일 읽기
                    var content = await Task.Run(() => File.ReadAllText(currentLogFile, Encoding.UTF8));
                    System.Diagnostics.Debug.WriteLine($"📄 파일 읽기 완료: {content.Length} 문자");
                    
                    UpdateLoadingStatus("로그 파싱 중...");
                    
                    // 백그라운드에서 파싱
                    var entries = await Task.Run(() => ParseDataLogEntries(content));
                    
                    if (loadMode == LogLoadMode.Recent)
                    {
                        entries = entries.TakeLast(recentCount).ToList();
                        System.Diagnostics.Debug.WriteLine($"📊 Recent: {entries.Count}개 로드");
                    }
                    else
                    {
                        entries = entries.Where(e => 
                            e.Timestamp.TimeOfDay >= filterStartTime && 
                            e.Timestamp.TimeOfDay <= filterEndTime).ToList();
                        
                        // 느린 쿼리 필터는 Settings에서 받아옴 (slowQueryOnly 필드 사용)
                        if (slowQueryOnly)
                            entries = entries.Where(e => e.ExecTimeMs >= 100).ToList();
                        
                        System.Diagnostics.Debug.WriteLine($"📊 All: 시간필터 후 {entries.Count}개 로드");
                    }

                    UpdateLoadingStatus($"{entries.Count}개 로그 추가 중...");

                    foreach (var entry in entries)
                        logEntries.Add(entry);

                    System.Diagnostics.Debug.WriteLine($"✅ logEntries에 {logEntries.Count}개 추가됨");

                    lastPosition = new FileInfo(currentLogFile).Length;
                    
                    isLoadingBatch = false;  // 일괄 로드 완료
                    ShowLoadingOverlay(false);
                    break;
            }
        }

        private List<DataLogEntry> ParseDataLogEntries(string content)
        {
            var entries = new List<DataLogEntry>();
            var matches = DataLogStartPattern.Matches(content);

            System.Diagnostics.Debug.WriteLine($"📊 DATA 로그 파싱: 파일 길이={content.Length}, 매칭 개수={matches.Count}");
            
            if (matches.Count == 0 && content.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 매칭 실패. 파일 첫 200자:\n{content.Substring(0, Math.Min(200, content.Length))}");
            }

            for (int i = 0; i < matches.Count; i++)
            {
                int startIndex = matches[i].Index;
                int endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
                string entryText = content.Substring(startIndex, endIndex - startIndex);
                var entry = ParseSingleDataEntry(entryText, matches[i]);
                if (entry != null) entries.Add(entry);
            }
            
            System.Diagnostics.Debug.WriteLine($"✅ DATA 로그 파싱 완료: {entries.Count}개 엔트리");
            return entries;
        }

        private DataLogEntry? ParseSingleDataEntry(string entryText, Match headerMatch)
        {
            try
            {
                string timestampStr = headerMatch.Groups[1].Value;
                string bizName = headerMatch.Groups[2].Value;

                // 제외할 비즈명 필터링
                if (ExcludedBizNames.Contains(bizName))
                    return null;

                // 밀리초 없음, 3자리, 7자리 모두 지원
                DateTime.TryParseExact(timestampStr, new[] { 
                    "MM-dd-yyyy HH:mm:ss.fffffff",  // 마이크로초 7자리
                    "MM-dd-yyyy HH:mm:ss.ffffff",   // 6자리
                    "MM-dd-yyyy HH:mm:ss.fffff",    // 5자리
                    "MM-dd-yyyy HH:mm:ss.ffff",     // 4자리
                    "MM-dd-yyyy HH:mm:ss.fff",      // 밀리초 3자리
                    "MM-dd-yyyy HH:mm:ss.ff",       // 2자리
                    "MM-dd-yyyy HH:mm:ss.f",        // 1자리
                    "MM-dd-yyyy HH:mm:ss"           // 밀리초 없음
                },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var timestamp);

                // exec.Time 추출
                var execTimeMatch = Regex.Match(entryText, @"exec\.Time\s*:\s*(\d{2}:\d{2}:\d{2}\.\d+)");
                string execTime = execTimeMatch.Success ? execTimeMatch.Groups[1].Value : "";

                // TXN_ID 추출
                var txnIdMatch = Regex.Match(entryText, @"TXN_ID\s*:\s*(\d+)");
                string txnId = txnIdMatch.Success ? txnIdMatch.Groups[1].Value : "";

                // XML 파라미터 추출
                var xmlMatch = Regex.Match(entryText, @"<NewDataSet>.*?</NewDataSet>", RegexOptions.Singleline);
                string parameterXml = xmlMatch.Success ? xmlMatch.Value : "";

                var fields = ParseXmlFields(parameterXml);
                LogFieldAnalyzer.AddDiscoveredFields(fields.Keys);

                return new DataLogEntry
                {
                    Timestamp = timestamp,
                    BizName = bizName,
                    ExecTime = execTime,
                    TxnId = txnId,
                    ParameterXml = parameterXml,
                    Fields = fields,
                    RawData = entryText.Trim()
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// 제외할 XML 노드 목록 (시스템 정보, 불필요한 메타데이터)
        /// </summary>
        private static readonly HashSet<string> ExcludeXmlNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "__BIZACTOR_INFO__", "__ERR_MSG_LANG__",
            "__TRACE_INFO__", "CLIENT_ID", "CLIENT_IP", "CLIENT_TIME",
            "NewDataSet", "IN_DATA", "OUT_DATA"
        };

        private Dictionary<string, string> ParseXmlFields(string xml)
        {
            var fields = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(xml)) return fields;

            try
            {
                var doc = XDocument.Parse(xml);
                foreach (var el in doc.Descendants())
                {
                    // 불필요한 노드 제외
                    if (ExcludeXmlNodes.Contains(el.Name.LocalName))
                        continue;

                    // 부모가 제외 대상인 경우도 제외
                    if (el.Parent != null && ExcludeXmlNodes.Contains(el.Parent.Name.LocalName) 
                        && el.Parent.Name.LocalName.StartsWith("__"))
                        continue;

                    if (!el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
                    {
                        string key = el.Name.LocalName;
                        if (fields.ContainsKey(key))
                            fields[key] += "," + el.Value.Trim();
                        else
                            fields[key] = el.Value.Trim();
                    }
                }
            }
            catch
            {
                foreach (Match m in Regex.Matches(xml, @"<(\w+)>([^<]*)</\1>"))
                {
                    string key = m.Groups[1].Value;
                    
                    // 불필요한 노드 제외
                    if (ExcludeXmlNodes.Contains(key))
                        continue;

                    string val = m.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(val))
                        fields[key] = fields.ContainsKey(key) ? fields[key] + "," + val : val;
                }
            }
            return fields;
        }

        #endregion

        #region 파일 감시

        private void StartFileWatcher()
        {
            StopFileWatcher();
            if (string.IsNullOrEmpty(logDirectory)) return;

            try
            {
                fileWatcher = new FileSystemWatcher(logDirectory)
                {
                    Filter = Path.GetFileName(currentLogFile),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                fileWatcher.Changed += (s, e) =>
                {
                    debounceTimer?.Dispose();
                    debounceTimer = new System.Threading.Timer(_ => Dispatcher.BeginInvoke(ReadNewLogEntries), null, 100, System.Threading.Timeout.Infinite);
                };
            }
            catch { }
        }

        private void StopFileWatcher()
        {
            fileWatcher?.Dispose();
            fileWatcher = null;
            debounceTimer?.Dispose();
            debounceTimer = null;
        }

        private void ReadNewLogEntries()
        {
            if (isReading) return;
            lock (fileLock) { if (isReading) return; isReading = true; }

            try
            {
                var fileInfo = new FileInfo(currentLogFile);
                if (fileInfo.Length <= lastPosition) { isReading = false; return; }

                using var stream = new FileStream(currentLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(lastPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var newContent = multiLineBuffer.ToString() + reader.ReadToEnd();
                multiLineBuffer.Clear();

                foreach (var entry in ParseDataLogEntries(newContent))
                    logEntries.Add(entry);

                lastPosition = fileInfo.Length;
            }
            catch { }
            finally { isReading = false; }
        }

        #endregion

        #region Collection Changed


        private System.Threading.Timer? scrollDebounceTimer;
        private System.Threading.Timer? statusDebounceTimer;
        private bool isLoadingBatch = false;  // 일괄 로드 중 플래그

        private void LogEntries_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems == null) return;

            if (isPaused)
            {
                foreach (DataLogEntry item in e.NewItems) pausedBuffer.Add(item);
            }
            else
            {
                foreach (DataLogEntry item in e.NewItems)
                {
                    item.RowNumber = displayEntries.Count + 1;
                    displayEntries.Add(item);

                    foreach (var kvp in tabDisplayEntries)
                        if (kvp.Key.IsMatch(item)) kvp.Value.Add(item);
                }

                // 스크롤 디바운싱 (일괄 로드 중에는 스킵, 마지막에 한 번만)
                if (isAutoScrollEnabled && !isLoadingBatch)
                {
                    scrollDebounceTimer?.Dispose();
                    scrollDebounceTimer = new System.Threading.Timer(_ =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (currentDataGrid?.Items.Count > 0)
                                currentDataGrid.ScrollIntoView(currentDataGrid.Items[^1]);
                        });
                    }, null, 100, System.Threading.Timeout.Infinite);
                }
            }
            
            // 상태 업데이트 디바운싱
            statusDebounceTimer?.Dispose();
            statusDebounceTimer = new System.Threading.Timer(_ =>
            {
                Dispatcher.BeginInvoke(() => 
                {
                    UpdateStatus();
                    UpdateTabCounts();
                });
            }, null, 200, System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// 탭 헤더의 카운트 업데이트
        /// </summary>
        private void UpdateTabCounts()
        {
            if (tabControl == null) return;

            foreach (TabItem tabItem in tabControl.Items)
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

        #endregion

        #region UI 이벤트

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabControl.SelectedItem is TabItem tab && tab.Tag is TabConfig cfg)
            {
                currentTabConfig = cfg;
                currentDataGrid = tabDataGrids.GetValueOrDefault(cfg);
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
        /// <summary>
        /// 현재 탭에만 AutoFit 적용 (외부에서도 호출 가능)
        /// </summary>
        public void ApplyAutoFit()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyAutoFitForCurrentTab();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

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

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            isPaused = !isPaused;
            btnPause.Content = isPaused ? "▶ 재개" : "⏸ 일시정지";

            if (!isPaused)
            {
                foreach (var entry in pausedBuffer)
                {
                    entry.RowNumber = displayEntries.Count + 1;
                    displayEntries.Add(entry);
                    foreach (var kvp in tabDisplayEntries)
                        if (kvp.Key.IsMatch(entry)) kvp.Value.Add(entry);
                }
                pausedBuffer.Clear();
            }
            UpdateStatus();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            logEntries.Clear();
            displayEntries.Clear();
            foreach (var entries in tabDisplayEntries.Values) entries.Clear();
            pausedBuffer.Clear();
            UpdateStatus();
        }

        private void BtnAutoScroll_Click(object sender, RoutedEventArgs e)
        {
            isAutoScrollEnabled = !isAutoScrollEnabled;
            btnAutoScroll.Content = isAutoScrollEnabled ? "⬇ 자동스크롤" : "⬇ 스크롤 OFF";
        }

        private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoFitForCurrentTab();
        }

        private void BtnFontMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int s) && s > 8)
            {
                txtFontSize.Text = (--s).ToString();
                foreach (var g in tabDataGrids.Values) g.FontSize = s;
            }
        }

        private void BtnFontPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int s) && s < 20)
            {
                txtFontSize.Text = (++s).ToString();
                foreach (var g in tabDataGrids.Values) g.FontSize = s;
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e) 
        { 
            if (e.Key == Key.Enter) 
            {
                ApplyAllFilters();
                e.Handled = true;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnClearSearch.Visibility = string.IsNullOrEmpty(txtSearch.Text) 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearch.Clear();
            ApplyAllFilters();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            ApplyAllFilters();
        }

        private void ApplySearch()
        {
            ApplyAllFilters();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (currentTabConfig == null || !tabDisplayEntries.TryGetValue(currentTabConfig, out var entries) || entries.Count == 0)
            {
                MessageBox.Show("내보낼 로그가 없습니다.", "알림"); return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"DATA_Log_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using var pkg = new ExcelPackage();
                var sheet = pkg.Workbook.Worksheets.Add("Log");
                sheet.Cells[1, 1].Value = "No"; sheet.Cells[1, 2].Value = "시간"; sheet.Cells[1, 3].Value = "비즈명";
                sheet.Cells[1, 4].Value = "실행시간"; sheet.Cells[1, 5].Value = "TXN_ID"; sheet.Cells[1, 6].Value = "파라미터";

                int row = 2;
                foreach (var entry in entries)
                {
                    sheet.Cells[row, 1].Value = entry.RowNumber;
                    sheet.Cells[row, 2].Value = entry.TimeString;
                    sheet.Cells[row, 3].Value = entry.BizName;
                    sheet.Cells[row, 4].Value = entry.ExecTime;
                    sheet.Cells[row, 5].Value = entry.TxnId;
                    sheet.Cells[row, 6].Value = entry.Summary;
                    row++;
                }
                sheet.Cells.AutoFitColumns();
                pkg.SaveAs(new FileInfo(dialog.FileName));
                MessageBox.Show("엑셀 저장 완료", "완료");
            }
            catch (Exception ex) { MessageBox.Show($"저장 실패: {ex.Message}", "오류"); }
        }

        #endregion

        #region 상태

        private void UpdateStatus()
        {
            int total = displayEntries.Count;
            int tabCount = currentTabConfig != null && tabDisplayEntries.TryGetValue(currentTabConfig, out var ent) ? ent.Count : 0;
            
            txtCount.Text = $" | 전체: {total:N0}건";
            txtTabCount.Text = $" | 현재 탭: {tabCount:N0}건";
            txtFile.Text = Path.GetFileName(currentLogFile);
            txtPausedCount.Text = isPaused && pausedBuffer.Count > 0 ? $"(대기: {pausedBuffer.Count}건)" : "";
            
            // 상태 표시: 실시간 감지가 활성화된 경우에만 "감시 중" 표시
            if (enableRealTimeWatch)
            {
                txtStatus.Text = isPaused ? "⏸ 일시정지" : "▶ 감시 중";
            }
            else
            {
                txtStatus.Text = "✅ 로드 완료";
            }
            
            // 모드 표시
            if (enableRealTimeWatch)
            {
                txtMode.Text = "📍 실시간 감지";
            }
            else
            {
                txtMode.Text = loadMode switch
                {
                    LogLoadMode.NewOnly => "📍 실행 이후 로그만",
                    LogLoadMode.Recent => $"📚 최근 {recentCount}개",
                    LogLoadMode.All => "📖 전체 로그",
                    _ => ""
                };
            }
        }

        #endregion

        #region 비즈 필터

        /// <summary>
        /// 비즈 콤보박스 열릴 때 - 발견된 비즈명 목록 갱신
        /// </summary>
        private void CboBizFilter_DropDownOpened(object? sender, EventArgs e)
        {
            // 현재 로그에서 발견된 비즈명들로 목록 갱신
            var newBizNames = logEntries.Select(x => x.BizName).Distinct().OrderBy(x => x).ToList();
            
            foreach (var bizName in newBizNames)
            {
                if (!discoveredBizNames.Contains(bizName))
                {
                    discoveredBizNames.Add(bizName);
                    bizFilterItems.Add(new BizFilterItem 
                    { 
                        Name = bizName, 
                        IsSelected = selectedBizNames.Count == 0 || selectedBizNames.Contains(bizName)
                    });
                }
            }
        }

        /// <summary>
        /// 비즈 체크박스 클릭
        /// </summary>
        private void BizFilterCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateBizFilterSelection();
        }

        /// <summary>
        /// 비즈 전체 선택
        /// </summary>
        private void BtnBizSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in bizFilterItems)
            {
                item.IsSelected = true;
            }
            UpdateBizFilterSelection();
        }

        /// <summary>
        /// 비즈 전체 해제
        /// </summary>
        private void BtnBizDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in bizFilterItems)
            {
                item.IsSelected = false;
            }
            UpdateBizFilterSelection();
        }

        /// <summary>
        /// 비즈 필터 선택 상태 업데이트
        /// </summary>
        private void UpdateBizFilterSelection()
        {
            // 선택된 비즈명 업데이트
            selectedBizNames.Clear();
            foreach (var item in bizFilterItems.Where(x => x.IsSelected))
            {
                selectedBizNames.Add(item.Name);
            }

            // 콤보박스 텍스트 업데이트
            if (bizFilterItems.Count == 0)
            {
                cboBizFilter.Text = "전체";
            }
            else if (selectedBizNames.Count == 0)
            {
                cboBizFilter.Text = "선택 없음";
            }
            else if (selectedBizNames.Count == bizFilterItems.Count)
            {
                cboBizFilter.Text = "전체";
            }
            else if (selectedBizNames.Count <= 2)
            {
                cboBizFilter.Text = string.Join(", ", selectedBizNames);
            }
            else
            {
                cboBizFilter.Text = $"{selectedBizNames.Count}개 선택";
            }

            // 필터 적용
            ApplyAllFilters();
        }

        #endregion

        #region 느린 쿼리 필터

        /// <summary>
        /// 느린 쿼리 체크박스 변경
        /// </summary>
        private void ChkSlowQuery_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAllFilters();
        }

        /// <summary>
        /// 느린 쿼리 임계값 TextBox Enter 키
        /// </summary>
        private void TxtSlowThreshold_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // 체크박스가 체크되어 있지 않으면 자동으로 체크
                if (chkSlowQuery.IsChecked != true)
                {
                    chkSlowQuery.IsChecked = true;
                }
                ApplyAllFilters();
                e.Handled = true;
            }
        }

        /// <summary>
        /// ms+ 텍스트 클릭 시 필터 적용
        /// </summary>
        private void TxtSlowThresholdApply_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 체크박스가 체크되어 있지 않으면 자동으로 체크
            if (chkSlowQuery.IsChecked != true)
            {
                chkSlowQuery.IsChecked = true;
            }
            ApplyAllFilters();
        }

        #endregion

        #region 시간 이동

        /// <summary>
        /// 시간 이동 텍스트박스 Enter 키
        /// </summary>
        private void TxtJumpTime_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
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
            
            // 필터링된 데이터 가져오기 (tabViews에서 필터가 적용된 뷰 사용)
            if (!tabViews.TryGetValue(currentTabConfig, out var view)) return;
            var filteredEntries = view.Cast<DataLogEntry>()
                .OrderBy(e => e.Timestamp)
                .ThenBy(e => e.RowNumber)
                .ToList();
            if (filteredEntries.Count == 0) return;

            string timeText = txtJumpTime.Text.Trim();
            if (string.IsNullOrEmpty(timeText)) return;

            // 시간 파싱
            TimeSpan targetTime;
            bool isMinuteOnlySearch = false;
            
            if (timeText.Length <= 2 && int.TryParse(timeText, out int hourOnly))
            {
                targetTime = new TimeSpan(hourOnly, 0, 0);
            }
            else if (timeText.Contains(':') && timeText.Split(':').Length == 2 && !timeText.Contains('.'))
            {
                // HH:mm 형식 (초가 없는 경우) → 분 단위로 검색
                if (TimeSpan.TryParse(timeText + ":00", out var parsed))
                {
                    targetTime = parsed;
                    isMinuteOnlySearch = true;
                }
                else
                {
                    MessageBox.Show("시간 형식이 올바르지 않습니다.\n예: 09:30, 14:00, 9", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (TimeSpan.TryParse(timeText, out var parsed2))
            {
                targetTime = parsed2;
            }
            else
            {
                MessageBox.Show("시간 형식이 올바르지 않습니다.\n예: 09:30, 14:00, 9", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 해당 시간 이후의 첫 번째 로그 찾기
            DataLogEntry? targetEntry = null;
            
            if (isMinuteOnlySearch)
            {
                var startTime = targetTime;
                var endTime = targetTime.Add(TimeSpan.FromSeconds(59.999));
                
                for (int i = 0; i < filteredEntries.Count; i++)
                {
                    var entryTime = filteredEntries[i].Timestamp.TimeOfDay;
                    if (entryTime >= startTime && entryTime <= endTime)
                    {
                        targetEntry = filteredEntries[i];
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < filteredEntries.Count; i++)
                {
                    if (filteredEntries[i].Timestamp.TimeOfDay >= targetTime)
                    {
                        targetEntry = filteredEntries[i];
                        break;
                    }
                }
            }

            if (targetEntry == null)
            {
                MessageBox.Show($"{targetTime:hh\\:mm} 이후의 로그가 필터링된 데이터에 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                currentDataGrid.ScrollIntoView(targetEntry);
                currentDataGrid.SelectedItem = targetEntry;
                currentDataGrid.Focus();
                
                txtStatus.Text = $"⏰ {targetTime:hh\\:mm} → {targetEntry.TimeString} (#{targetEntry.RowNumber})";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"시간 이동 실패: {ex.Message}");
            }
        }

        #endregion

        #region 통합 필터

        /// <summary>
        /// 모든 필터 조건 적용 (복합 검색 지원: 쉼표=OR, 플러스=AND)
        /// </summary>
        private void ApplyAllFilters()
        {
            if (currentTabConfig == null || !tabViews.TryGetValue(currentTabConfig, out var view)) return;

            string searchText = txtSearch.Text.Trim();
            bool slowQueryEnabled = chkSlowQuery.IsChecked == true;
            int threshold = int.TryParse(txtSlowThreshold.Text, out int t) ? t : 100;
            
            // 비즈 필터가 활성화되어 있는지 확인 (항목이 있고, 전체 선택이 아닌 경우)
            bool bizFilterActive = bizFilterItems.Count > 0 && selectedBizNames.Count < bizFilterItems.Count;
            // 전체 해제인 경우 (선택된 것이 없으면 아무것도 표시하지 않음)
            bool noBizSelected = bizFilterItems.Count > 0 && selectedBizNames.Count == 0;

            view.Filter = obj =>
            {
                if (obj is not DataLogEntry entry) return false;

                // 비즈 필터: 전체 해제 시 아무것도 표시하지 않음
                if (noBizSelected)
                    return false;
                
                // 비즈 필터: 일부만 선택된 경우 필터링
                if (bizFilterActive && !selectedBizNames.Contains(entry.BizName))
                    return false;

                // 느린 쿼리 필터
                if (slowQueryEnabled && entry.ExecTimeMs < threshold)
                    return false;

                // 검색 필터 (복합 검색)
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!MatchesComplexSearch(entry, searchText))
                        return false;
                }

                return true;
            };

            view.Refresh();
            UpdateStatus();
        }

        /// <summary>
        /// 복합 검색 매칭 (쉼표=OR, 플러스=AND)
        /// 예: "GetData+1234" → GetData AND 1234
        /// 예: "Insert,Update" → Insert OR Update
        /// 예: "GetData+1234,Update" → (GetData AND 1234) OR Update
        /// ValueMapping이 적용된 디스플레이 값으로도 검색 가능
        /// </summary>
        private bool MatchesComplexSearch(DataLogEntry entry, string searchText)
        {
            // 전체 검색 대상 텍스트 생성 (기본 필드)
            var searchTargetBuilder = new StringBuilder();
            searchTargetBuilder.Append($"{entry.BizName} {entry.TxnId} {entry.ParameterXml} {entry.ExecTime}");

            // ValueMapping이 적용된 필드들의 변환된 값도 검색 대상에 추가
            var dataSettings = UnifiedPresetManager.CurrentPreset?.DataSettings;
            if (dataSettings?.ColumnFields != null)
            {
                foreach (var field in dataSettings.ColumnFields.Where(f => f.IsEnabled && !string.IsNullOrEmpty(f.ValueMapping)))
                {
                    string? originalValue = null;
                    
                    if (field.IsParameter && entry.Fields.TryGetValue(field.FieldName, out var paramValue))
                    {
                        originalValue = paramValue;
                    }
                    else if (!field.IsParameter)
                    {
                        // 기본 필드의 경우
                        originalValue = field.FieldName switch
                        {
                            "BizName" => entry.BizName,
                            "TxnId" => entry.TxnId,
                            "ExecTime" => entry.ExecTime,
                            "ClientId" => entry.ClientId,
                            "ClientIp" => entry.ClientIp,
                            _ => null
                        };
                    }
                    
                    if (!string.IsNullOrEmpty(originalValue))
                    {
                        var displayValue = ValueMappingConverter.ConvertValue(originalValue, field.ValueMapping);
                        if (displayValue != originalValue)
                        {
                            searchTargetBuilder.Append($" {displayValue}");
                        }
                    }
                }
            }

            string searchTarget = searchTargetBuilder.ToString();

            // 쉼표로 분리 (OR 조건)
            var orConditions = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var orCondition in orConditions)
            {
                // 플러스로 분리 (AND 조건)
                var andConditions = orCondition.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries);

                bool allMatch = true;
                foreach (var andCondition in andConditions)
                {
                    if (!searchTarget.Contains(andCondition.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }

                // 하나의 OR 조건이라도 만족하면 true
                if (allMatch)
                    return true;
            }

            return false;
        }

        #endregion

        #region 로딩 오버레이

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

        #endregion
    }
}
