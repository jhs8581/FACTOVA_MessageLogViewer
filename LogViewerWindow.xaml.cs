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
using System.Windows.Data;
using System.Windows.Media;

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

        // 탭별 ListView 및 View 관리
        private Dictionary<TabConfig, ListView> tabListViews = new();
        private Dictionary<TabConfig, ICollectionView> tabViews = new();
        private Dictionary<TabConfig, ObservableCollection<LogEntry>> tabDisplayEntries = new();
        private ListView? currentListView;
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


        public LogViewerWindow(string logFilePath, DateTime date, LogLoadMode mode, int count)
        {
            InitializeComponent();

            currentLogFile = logFilePath;
            logDirectory = Path.GetDirectoryName(logFilePath) ?? "";
            selectedDate = date;
            loadMode = mode;
            recentCount = count;

            txtLogFolder.Text = $"({Path.GetFileName(logFilePath)})";

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
            tabListViews.Clear();
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

            // ListView 생성
            var listView = CreateListView(tabConfig, entries);
            tabListViews[tabConfig] = listView;

            // View 생성 및 필터 설정
            var view = CollectionViewSource.GetDefaultView(entries);
            view.Filter = item => FilterLogEntry(item, tabConfig);
            tabViews[tabConfig] = view;

            listView.ItemsSource = view;

            // 탭 헤더 (카운트 표시 포함)
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var headerText = new TextBlock { Text = tabConfig.Name };
            var countText = new TextBlock 
            { 
                Text = " (0)", 
                Foreground = new SolidColorBrush(Colors.Gray),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
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
                Content = listView,
                Tag = tabConfig,
                ToolTip = tooltip
            };

            return tabItem;
        }

        /// <summary>
        /// ListView 생성
        /// </summary>
        private ListView CreateListView(TabConfig tabConfig, ObservableCollection<LogEntry> entries)
        {
            var listView = new ListView
            {
                FontSize = 11,
                Margin = new Thickness(0)
            };

            // 가상화 설정
            VirtualizingPanel.SetIsVirtualizing(listView, true);
            VirtualizingPanel.SetVirtualizationMode(listView, VirtualizationMode.Recycling);
            VirtualizingPanel.SetCacheLength(listView, new VirtualizationCacheLength(20));
            ScrollViewer.SetIsDeferredScrollingEnabled(listView, true);

            // GridView 생성
            var gridView = new GridView();

            // 기본 컬럼들
            gridView.Columns.Add(CreateColumn("시간", "TimeString", 100, fontFamily: "Consolas"));
            gridView.Columns.Add(CreateColumn("구분", "DirectionText", 50, fontWeight: FontWeights.Bold, hAlign: HorizontalAlignment.Center));
            gridView.Columns.Add(CreateColumn("MsgId", "MessageId", 60, fontFamily: "Consolas", hAlign: HorizontalAlignment.Center));

            // 통합 로그 탭에만 "분류" 컬럼 추가
            if (tabConfig.IsIntegrated)
            {
                gridView.Columns.Add(CreateColumn("분류", "MatchedTabName", 100, foregroundColor: "#1565C0"));
            }

            // 동적 컬럼 추가
            var settings = ColumnSettingsManager.CurrentSettings;
            foreach (var fieldConfig in settings.ColumnFields)
            {
                var column = CreateDynamicColumn(fieldConfig, listView);
                gridView.Columns.Add(column);
            }

            // Summary 컬럼 (항상 마지막)
            gridView.Columns.Add(CreateColumn("주요내용", "Summary", 500, trimming: true));

            listView.View = gridView;

            // ItemContainerStyle
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(ListViewItem.BackgroundProperty, new Binding("BackgroundBrush")));
            style.Setters.Add(new Setter(ListViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(ListViewItem.PaddingProperty, new Thickness(8, 9, 8, 9)));  // 행 패딩 증가
            style.Setters.Add(new Setter(ListViewItem.MarginProperty, new Thickness(0, 2, 0, 0)));   // 행 간격 증가
            listView.ItemContainerStyle = style;

            // ColumnHeaderContainerStyle - 헤더 스타일 개선
            var headerStyle = new Style(typeof(GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.FontSizeProperty, 14.0));  // 폰트 크기 14
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(240, 240, 240))));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.ForegroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.PaddingProperty, new Thickness(12, 10, 12, 10)));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 220, 220))));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            headerStyle.Setters.Add(new Setter(GridViewColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            
            if (gridView is GridView gv)
            {
                gv.ColumnHeaderContainerStyle = headerStyle;
            }

            // ItemsPanel
            var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            listView.ItemsPanel = panelTemplate;

            return listView;
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
            factory.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            
            // 컬럼 간격 확보를 위한 좌우 여백 (12px로 증가)
            factory.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 12, 0));
            
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
            
            // 컬럼 간격 확보를 위한 좌우 여백 (12px로 증가)
            factory.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 12, 0));

            // Fields 딕셔너리에서 값 가져오는 바인딩 (FontSize는 ListView에서 상속)
            var binding = new System.Windows.Data.Binding($"Fields[{config.FieldName}]");
            
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

        private void LoadLogs()
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
                    LoadRecentLogs();
                    break;

                case LogLoadMode.All:
                    LoadAllLogs();
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

        private void LoadRecentLogs()
        {
            try
            {
                var content = File.ReadAllText(currentLogFile, Encoding.UTF8);
                var entries = ParseLogEntries(content);

                // 최근 N개만
                var recentEntries = entries.TakeLast(recentCount).ToList();

                System.Diagnostics.Debug.WriteLine($"📖 최근 {recentEntries.Count}개 로그 로드 중...");

                // 일괄 추가로 UI 갱신 최소화
                logManager.AddLogEntries(recentEntries);

                lastPosition = new FileInfo(currentLogFile).Length;

                System.Diagnostics.Debug.WriteLine($"✅ 로드 완료: {logManager.LogEntries.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 최근 로그 로드 실패: {ex.Message}");
            }
        }

        private void LoadAllLogs()
        {
            try
            {
                var content = File.ReadAllText(currentLogFile, Encoding.UTF8);
                
                // 디버그: 파일 내용 일부 출력
                System.Diagnostics.Debug.WriteLine($"📄 파일 크기: {content.Length} 문자");
                if (content.Length > 0)
                {
                    var firstLines = content.Substring(0, Math.Min(500, content.Length));
                    System.Diagnostics.Debug.WriteLine($"📄 파일 시작:\n{firstLines}");
                }
                
                var entries = ParseLogEntries(content);

                System.Diagnostics.Debug.WriteLine($"📖 전체 {entries.Count}개 로그 로드 중...");

                // 일괄 추가로 UI 갱신 최소화
                logManager.AddLogEntries(entries);

                lastPosition = new FileInfo(currentLogFile).Length;

                System.Diagnostics.Debug.WriteLine($"✅ 로드 완료: {logManager.LogEntries.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 전체 로그 로드 실패: {ex.Message}");
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
            foreach (var view in tabViews.Values)
            {
                view?.Refresh();
            }
            UpdateTabCounts();
        }

        /// <summary>
        /// 탭 헤더의 카운트 업데이트
        /// </summary>
        private void UpdateTabCounts()
        {
            foreach (TabItem tabItem in tabControlLogs.Items)
            {
                if (tabItem.Tag is TabConfig tabConfig && 
                    tabItem.Header is StackPanel headerPanel &&
                    headerPanel.Children.Count > 1 &&
                    headerPanel.Children[1] is TextBlock countText)
                {
                    if (tabDisplayEntries.TryGetValue(tabConfig, out var entries))
                    {
                        countText.Text = $" ({entries.Count})";
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
                
                if (tabListViews.TryGetValue(tabConfig, out var listView))
                {
                    currentListView = listView;
                }

                if (tabViews.TryGetValue(tabConfig, out var view))
                {
                    logView = view;
                }

                // 탭 인덱스는 메모리에만 저장 (창 닫을 때 저장)
                lastSelectedTabIndex = tabControlLogs.SelectedIndex;

                UpdateStatus();
            }
        }

        private int lastSelectedTabIndex = 0;

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
                    }

                    pausedBuffer.Clear();
                    logView?.Refresh();
                    AutoScrollToBottom();
                }

                UpdateStatus();
                System.Diagnostics.Debug.WriteLine("🟢 재개");
            }
        }

        private void AutoScrollToBottom()
        {
            var listView = currentListView;
            if (listView != null && listView.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        listView.ScrollIntoView(listView.Items[listView.Items.Count - 1]);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateStatus()
        {
            int displayCount = 0;
            int filteredCount = 0;

            // 현재 탭의 카운트 표시
            if (currentTabConfig != null && tabDisplayEntries.TryGetValue(currentTabConfig, out var entries))
            {
                displayCount = entries.Count;
                filteredCount = displayCount;  // 기본값
                
                // 필터링된 카운트는 검색 필터가 있을 때만 표시 (계산은 비동기로)
                if (!string.IsNullOrWhiteSpace(txtSearch?.Text) || 
                    chkSendOnly?.IsChecked == true || 
                    chkRecvOnly?.IsChecked == true)
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

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // 디바운싱: 300ms 후에 검색 실행
            searchDebounceTimer?.Dispose();
            searchDebounceTimer = new System.Threading.Timer(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    RefreshAllTabViews();
                    UpdateStatus();
                });
            }, null, 300, System.Threading.Timeout.Infinite);
        }

        private System.Threading.Timer? searchDebounceTimer;

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
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
            // 모든 탭의 ListView에 폰트 사이즈 적용
            foreach (var listView in tabListViews.Values)
            {
                listView.FontSize = size;
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
            foreach (var listView in tabListViews.Values)
            {
                listView.FontSize = size;
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
            try
            {
                // 모든 탭의 ListView에 접근하여 컬럼 너비를 AUTO로 설정
                foreach (var listView in tabListViews.Values)
                {
                    if (listView.View is GridView gridView)
                    {
                        foreach (var column in gridView.Columns)
                        {
                            // AUTO 모드로 설정 (NaN)
                            column.Width = double.NaN;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine("✅ Auto Fit 적용: 모든 컬럼 너비를 AUTO로 설정");
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
            // UI가 완전히 렌더링된 후에 Auto Fit 적용
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 모든 탭의 ListView에 접근하여 컬럼 너비를 AUTO로 설정
                    foreach (var listView in tabListViews.Values)
                    {
                        if (listView.View is GridView gridView)
                        {
                            foreach (var column in gridView.Columns)
                            {
                                // AUTO 모드로 설정 (NaN)
                                column.Width = double.NaN;
                            }
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("✅ 초기 Auto Fit 적용 완료");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 초기 Auto Fit 실패: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // OnClosing에서 처리됨
        }
    }
}
