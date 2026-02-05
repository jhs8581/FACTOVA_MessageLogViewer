using FACTOVA_MessageLogViewer.Converters;
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
using System.Windows.Input;
using System.Windows.Media;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace FACTOVA_MessageLogViewer
{
    public partial class FLLogViewerControl : UserControl
    {
        private ObservableCollection<FLLogEntry> logEntries = new();
        private Dictionary<string, ObservableCollection<FLLogEntry>> tabLogEntries = new();
        private Dictionary<string, ICollectionView> tabLogViews = new();
        private ICollectionView? currentLogView = null;
        private DataGrid? currentDataGrid = null;
        
        private string logDirectory = "";
        private DateTime selectedDate = DateTime.Today;
        private List<string> loadedFiles = new();
        private HashSet<string> loadedHours = new();
        
        private bool isAutoScrollEnabled = true;
        private bool isInitialized = false;
        private bool isTabChanging = false;

        // 실시간 감지용 FileSystemWatcher
        private FileSystemWatcher? fileWatcher;
        private System.Timers.Timer? watcherDebounceTimer;
        private HashSet<string> pendingWatcherFiles = new();
        private bool isRealTimeWatchEnabled = true;

        // 태그 필터 관련
        private ObservableCollection<BizFilterItem> tagFilterItems = new();
        private HashSet<string> discoveredTagNames = new();
        private HashSet<string> selectedTagNames = new();

        // 로그 파싱 패턴: 2026-01-28 07:16:54.214 [Debug] [Module.Name] [TagName] (Type) : Value
        private static readonly Regex LogLinePattern = new Regex(
            @"^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})\s+\[(\w+)\]\s+\[([^\]]+)\]\s+\[([^\]]+)\]\s+\(([^)]+)\)\s*:\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // 파일명에서 날짜/시간 추출 패턴: _MMDDYY.log (MM=월, DD=일, YY=시)
        private static readonly Regex FileNamePattern = new Regex(
            @"_(\d{2})(\d{2})(\d{2})\.log$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public FLLogViewerControl()
        {
            InitializeComponent();
            cboTagFilter.ItemsSource = tagFilterItems;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSavedFontSize();
        }

        /// <summary>
        /// 현재 로드된 로그 엔트리 반환 (프리셋 설정에서 사용)
        /// </summary>
        public IEnumerable<FLLogEntry> GetLogEntries()
        {
            return logEntries;
        }

        /// <summary>
        /// 설정으로 초기화 (MainWindow에서 호출) - 실시간 감지 없음
        /// </summary>
        public async Task InitializeAsync(string directory, DateTime date)
        {
            await InitializeAsync(directory, date, false);
        }

        /// <summary>
        /// 설정으로 초기화 (MainWindow에서 호출) - 실시간 감지 옵션 포함
        /// </summary>
        public async Task InitializeAsync(string directory, DateTime date, bool enableRealTimeWatch)
        {
            ShowLoadingOverlay(true);
            UpdateLoadingStatus("초기화 중...");
            
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            try
            {
                // 기존 FileSystemWatcher 정리
                StopFileWatcher();

                if (isInitialized)
                {
                    logEntries.Clear();
                    loadedFiles.Clear();
                    loadedHours.Clear();
                    tabLogEntries.Clear();
                    tabLogViews.Clear();
                }

                logDirectory = directory;
                selectedDate = date;
                txtLogFolder.Text = $"({date:MM/dd} - {Path.GetFileName(directory)})";
                txtDirectory.Text = directory;

                UpdateLoadingStatus($"F/L 로그 파일 검색 중... ({date:MM월 dd일})");
                
                // F/L 로그 파일 검색 및 로드
                await LoadFLLogsAsync();

                // 탭 생성
                CreateTabs();

                UpdateStatus();
                isInitialized = true;

                // 실시간 감지 시작
                if (enableRealTimeWatch)
                {
                    StartFileWatcher();
                }

                // Auto Fit 적용
                Dispatcher.BeginInvoke(new Action(ApplyAutoFit), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            finally
            {
                ShowLoadingOverlay(false);
            }
        }

        /// <summary>
        /// 프리셋 설정에 따라 탭 생성
        /// </summary>
        private void CreateTabs()
        {
            tabControlLogs.Items.Clear();
            tabLogEntries.Clear();
            tabLogViews.Clear();

            var flSettings = FLPresetManager.CurrentSettings;
            var enabledTabs = flSettings.TabSettings?.Tabs?.Where(t => t.IsEnabled).ToList() 
                              ?? new List<FLTabConfig>();

            // 탭이 없으면 기본 통합 탭 생성
            if (enabledTabs.Count == 0)
            {
                enabledTabs.Add(new FLTabConfig
                {
                    Name = "📊 전체 로그",
                    IsIntegrated = true,
                    IsEnabled = true
                });
            }

            foreach (var tabConfig in enabledTabs)
            {
                var tabItem = CreateTabItem(tabConfig);
                tabControlLogs.Items.Add(tabItem);
            }

            // 첫 번째 탭 선택
            if (tabControlLogs.Items.Count > 0)
            {
                tabControlLogs.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// 개별 탭 아이템 생성
        /// </summary>
        private TabItem CreateTabItem(FLTabConfig tabConfig)
        {
            // 탭별 데이터 컬렉션 생성
            var tabEntries = new ObservableCollection<FLLogEntry>();
            var tabView = CollectionViewSource.GetDefaultView(tabEntries);
            
            tabLogEntries[tabConfig.Name] = tabEntries;
            tabLogViews[tabConfig.Name] = tabView;

            // 탭에 맞는 로그 필터링
            foreach (var entry in logEntries)
            {
                if (IsEntryMatchesTab(entry, tabConfig))
                {
                    tabEntries.Add(entry);
                }
            }

            // DataGrid 생성
            var dataGrid = CreateDataGrid();
            dataGrid.ItemsSource = tabView;

            var tabItem = new TabItem
            {
                Header = $"{tabConfig.Name} ({tabEntries.Count})",
                Content = dataGrid,
                Tag = tabConfig
            };

            return tabItem;
        }

        /// <summary>
        /// DataGrid 생성 (탭별)
        /// </summary>
        private DataGrid CreateDataGrid()
        {
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                CanUserSortColumns = true,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true,
                FontSize = GetCurrentFontSize(),
                Background = Brushes.White
            };

            // 기본 컬럼 추가 (레벨, 모듈 제외)
            dataGrid.Columns.Add(CreateTextColumn("#", "RowNumber", 60, HorizontalAlignment.Center, "#666666", "Consolas"));
            dataGrid.Columns.Add(CreateTextColumn("시간대", "Hour", 50, HorizontalAlignment.Center, "#FF9800", null, FontWeights.Bold));
            dataGrid.Columns.Add(CreateTextColumn("시간", "TimeString", 100, HorizontalAlignment.Left, null, "Consolas"));
            dataGrid.Columns.Add(CreateTextColumn("태그명", "TagName", 300, HorizontalAlignment.Left, null, "Consolas"));
            dataGrid.Columns.Add(CreateTextColumn("태그설명", "TagDescription", 120, HorizontalAlignment.Left, "#7B1FA2"));
            dataGrid.Columns.Add(CreateTextColumn("타입", "DataType", 70, HorizontalAlignment.Center, "#666666"));

            // Boolean 값 컬럼 (Boolean 타입에만 표시)
            dataGrid.Columns.Add(CreateBooleanValueColumn());

            // 프리셋의 필드 설정에 따라 동적 컬럼 추가 (Structure 타입용)
            var columnFields = FLPresetManager.GetColumnFields();
            foreach (var fieldConfig in columnFields)
            {
                var fieldColumn = CreateFieldColumn(fieldConfig);
                dataGrid.Columns.Add(fieldColumn);
            }
            
            // Structure 값 컬럼 (마지막)
            var valueColumn = CreateStructureValueColumn();
            valueColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            dataGrid.Columns.Add(valueColumn);

            dataGrid.MouseDoubleClick += DataGrid_MouseDoubleClick;
            dataGrid.PreviewKeyDown += DataGrid_PreviewKeyDown;

            // DataGrid Row 스타일 (배경색)
            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, new Binding("BackgroundBrush") { Mode = BindingMode.OneTime }));
            rowStyle.Setters.Add(new Setter(DataGridRow.MinHeightProperty, 26.0));
            dataGrid.RowStyle = rowStyle;

            return dataGrid;
        }

        /// <summary>
        /// Boolean 값 전용 컬럼 생성 (ON/OFF)
        /// </summary>
        private DataGridTextColumn CreateBooleanValueColumn()
        {
            var column = new DataGridTextColumn
            {
                Header = "Boolean",
                Width = new DataGridLength(70)
            };

            var binding = new Binding()
            {
                Mode = BindingMode.OneTime,
                Converter = new FLBooleanValueConverter()
            };
            column.Binding = binding;

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 0, 5, 0)));

            column.ElementStyle = style;
            return column;
        }

        /// <summary>
        /// Structure 값 전용 컬럼 생성
        /// </summary>
        private DataGridTextColumn CreateStructureValueColumn()
        {
            var column = new DataGridTextColumn
            {
                Header = "Structure 값"
            };

            var binding = new Binding()
            {
                Mode = BindingMode.OneTime,
                Converter = new FLStructureValueConverter()
            };
            column.Binding = binding;

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 0, 5, 0)));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1A, 0x23, 0x7E)))); // 진한 파란색

            column.ElementStyle = style;
            return column;
        }

        /// <summary>
        /// Structure 필드용 동적 컬럼 생성
        /// </summary>
        private DataGridTextColumn CreateFieldColumn(FLFieldConfig fieldConfig)
        {
            // 헤더에서 언더스코어를 두 개로 변경 (WPF AccessKey 문제 해결)
            var headerText = (string.IsNullOrEmpty(fieldConfig.DisplayName) ? fieldConfig.FieldName : fieldConfig.DisplayName)
                .Replace("_", "__");
            
            var column = new DataGridTextColumn
            {
                Header = headerText,
                Width = fieldConfig.ColumnWidth > 0 ? new DataGridLength(fieldConfig.ColumnWidth) : new DataGridLength(80)
            };

            // Fields 딕셔너리 전체를 컨버터에 전달 (키가 없는 경우 처리)
            var binding = new Binding("Fields")
            {
                Mode = BindingMode.OneTime,
                Converter = new FLFieldValueConverter(fieldConfig)
            };
            column.Binding = binding;

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 0, 5, 0)));

            column.ElementStyle = style;
            return column;
        }

        private DataGridTextColumn CreateTextColumn(string header, string binding, double width, 
            HorizontalAlignment alignment, string? foreground = null, string? fontFamily = null, FontWeight? fontWeight = null)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding) { Mode = BindingMode.OneTime },
                Width = width > 0 ? new DataGridLength(width) : DataGridLength.Auto
            };

            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, alignment));
            style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(5, 0, 5, 0)));
            
            if (!string.IsNullOrEmpty(foreground))
                style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground))));
            if (!string.IsNullOrEmpty(fontFamily))
                style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily(fontFamily)));
            if (fontWeight.HasValue)
                style.Setters.Add(new Setter(TextBlock.FontWeightProperty, fontWeight.Value));

            column.ElementStyle = style;
            return column;
        }

        /// <summary>
        /// 로그 엔트리가 탭 조건에 맞는지 확인
        /// </summary>
        private bool IsEntryMatchesTab(FLLogEntry entry, FLTabConfig tabConfig)
        {
            // 통합 탭이면 모두 표시
            if (tabConfig.IsIntegrated) return true;

            // 조건 그룹이 없으면 표시 안함
            if (tabConfig.ConditionGroups == null || tabConfig.ConditionGroups.Count == 0)
                return false;

            // 그룹 간 OR 조건
            foreach (var group in tabConfig.ConditionGroups)
            {
                if (group.TagNames == null || group.TagNames.Count == 0)
                    continue;

                // 그룹 내 AND 조건 - 태그명이 그룹의 태그 중 하나와 일치하면 됨
                if (group.TagNames.Any(tagName => 
                    entry.TagName.Contains(tagName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 탭 선택 변경 이벤트
        /// </summary>
        private void TabControlLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isTabChanging) return;

            if (tabControlLogs.SelectedItem is TabItem tabItem)
            {
                currentDataGrid = tabItem.Content as DataGrid;
                if (tabLogViews.TryGetValue(tabItem.Tag?.ToString() ?? "", out var view))
                {
                    currentLogView = view;
                }
                else if (tabItem.Tag is FLTabConfig config && tabLogViews.TryGetValue(config.Name, out view))
                {
                    currentLogView = view;
                }

                ApplyFilters();
                UpdateStatus();
            }
        }


        /// <summary>
        /// F/L 로그 파일 검색 및 로드
        /// </summary>
        private async Task LoadFLLogsAsync()
        {
            if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory))
            {
                UpdateLoadingStatus("디렉토리가 존재하지 않습니다.");
                return;
            }

            try
            {
                UpdateLoadingStatus("파일 검색 중...");

                // 선택한 날짜의 MM, DD 추출
                var targetMonth = selectedDate.Month.ToString("D2");
                var targetDay = selectedDate.Day.ToString("D2");

                // LGE로 시작하지 않는 .log 파일 중 _MMDDYY.log 형식이고 선택한 날짜와 일치하는 파일 찾기
                var allLogFiles = await Task.Run(() =>
                {
                    return Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                        .Where(f =>
                        {
                            var fileName = Path.GetFileName(f);
                            // LGE로 시작하지 않아야 함
                            if (fileName.StartsWith("LGE", StringComparison.OrdinalIgnoreCase))
                                return false;
                            
                            // 파일명 패턴 확인 및 날짜 필터링
                            var match = FileNamePattern.Match(fileName);
                            if (!match.Success) return false;
                            
                            // MM, DD가 선택한 날짜와 일치하는지 확인
                            var fileMonth = match.Groups[1].Value;
                            var fileDay = match.Groups[2].Value;
                            return fileMonth == targetMonth && fileDay == targetDay;
                        })
                        .OrderBy(f => f)
                        .ToList();
                });

                if (allLogFiles.Count == 0)
                {
                    UpdateLoadingStatus($"F/L 로그 파일을 찾을 수 없습니다. ({selectedDate:MM/dd})");
                    txtFileCount.Text = "파일: 0개";
                    return;
                }

                loadedFiles = allLogFiles;
                txtFileCount.Text = $"파일: {allLogFiles.Count}개";

                // 시간대별로 그룹화
                var hourGroups = allLogFiles
                    .Select(f => new { File = f, Match = FileNamePattern.Match(Path.GetFileName(f)) })
                    .Where(x => x.Match.Success)
                    .GroupBy(x => x.Match.Groups[3].Value) // 시간 부분 (YY = 시간)
                    .OrderBy(g => g.Key)
                    .ToList();

                // 시간대 콤보박스 업데이트
                cboHourFilter.Items.Clear();
                cboHourFilter.Items.Add(new ComboBoxItem { Content = "전체", IsSelected = true });
                foreach (var group in hourGroups)
                {
                    cboHourFilter.Items.Add(new ComboBoxItem { Content = $"{group.Key}시 ({group.Count()}개)" });
                    loadedHours.Add(group.Key);
                }

                // 모든 파일 로드
                int totalLoaded = 0;
                var allEntries = new List<FLLogEntry>();

                foreach (var file in allLogFiles)
                {
                    UpdateLoadingStatus($"파일 로드 중... ({totalLoaded + 1}/{allLogFiles.Count})");
                    
                    var entries = await Task.Run(() => ParseFLLogFile(file));
                    allEntries.AddRange(entries);
                    totalLoaded++;
                }

                // 시간순 정렬
                allEntries = allEntries.OrderBy(e => e.Timestamp).ToList();


                // 프리셋에서 태그 설명 적용
                var tagDescriptions = FLPresetManager.GetTagDescriptions();

                // 행 번호 부여 및 태그 설명 적용
                for (int i = 0; i < allEntries.Count; i++)
                {
                    allEntries[i].RowNumber = i + 1;
                    
                    if (tagDescriptions.TryGetValue(allEntries[i].TagName, out var description))
                    {
                        allEntries[i].TagDescription = description;
                    }
                }

                UpdateLoadingStatus($"{allEntries.Count}개 로그 추가 중...");

                // UI에 추가
                foreach (var entry in allEntries)
                {
                    logEntries.Add(entry);
                }

                System.Diagnostics.Debug.WriteLine($"✅ F/L 로그 로드 완료: {logEntries.Count}개");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ F/L 로그 로드 실패: {ex.Message}");
                MessageBox.Show($"로그 로드 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// F/L 로그 파일 파싱 (멀티라인 Structure 지원)
        /// </summary>
        private List<FLLogEntry> ParseFLLogFile(string filePath)
        {
            var entries = new List<FLLogEntry>();
            var fileName = Path.GetFileName(filePath);

            // 파일명에서 시간대 추출
            var match = FileNamePattern.Match(fileName);
            string hour = match.Success ? match.Groups[3].Value : "";

            // Structure 필드 패턴: [필드명] : 값
            var fieldPattern = new Regex(@"^\[([^\]]+)\]\s*:\s*(.*)$", RegexOptions.Compiled);

            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                FLLogEntry? currentEntry = null;
                var rawLineBuilder = new StringBuilder();

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var logMatch = LogLinePattern.Match(line);
                    if (logMatch.Success)
                    {
                        // 이전 엔트리 저장
                        if (currentEntry != null)
                        {
                            currentEntry.RawLine = rawLineBuilder.ToString();
                            entries.Add(currentEntry);
                        }

                        // 새 엔트리 시작
                        currentEntry = new FLLogEntry
                        {
                            SourceFile = fileName,
                            Hour = hour,
                            Level = logMatch.Groups[2].Value,
                            ModuleName = logMatch.Groups[3].Value,
                            TagName = logMatch.Groups[4].Value,
                            DataType = logMatch.Groups[5].Value,
                            Value = logMatch.Groups[6].Value.Trim()
                        };

                        rawLineBuilder.Clear();
                        rawLineBuilder.AppendLine(line);

                        // 타임스탬프 파싱
                        if (DateTime.TryParse(logMatch.Groups[1].Value, out DateTime timestamp))
                        {
                            currentEntry.Timestamp = timestamp;
                        }
                        else
                        {
                            currentEntry.Timestamp = DateTime.MinValue;
                        }
                    }
                    else if (currentEntry != null && currentEntry.IsStructure)
                    {
                        // Structure의 필드 라인 파싱
                        rawLineBuilder.AppendLine(line);
                        
                        var fieldMatch = fieldPattern.Match(line.Trim());
                        if (fieldMatch.Success)
                        {
                            var fieldName = fieldMatch.Groups[1].Value.Trim();
                            var fieldValue = fieldMatch.Groups[2].Value.Trim();
                            
                            if (!currentEntry.Fields.ContainsKey(fieldName))
                            {
                                currentEntry.Fields[fieldName] = fieldValue;
                            }
                        }
                    }
                }

                // 마지막 엔트리 저장
                if (currentEntry != null)
                {
                    currentEntry.RawLine = rawLineBuilder.ToString();
                    entries.Add(currentEntry);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 파일 파싱 오류 ({fileName}): {ex.Message}");
            }

            return entries;
        }

        #region 필터링

        private void ApplyFilters()
        {
            if (currentLogView == null) return;

            var searchText = txtSearch.Text?.Trim() ?? "";
            var typeFilter = (cboTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체";
            var valueFilter = (cboValueFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체";
            var hourFilter = (cboHourFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "전체";

            // 태그 필터 - 선택된 것이 없거나 전체 선택이면 필터 미적용
            bool applyTagFilter = selectedTagNames.Count > 0 && selectedTagNames.Count < discoveredTagNames.Count;

            currentLogView.Filter = item =>
            {
                if (item is not FLLogEntry entry) return false;

                // 태그 필터
                if (applyTagFilter && !selectedTagNames.Contains(entry.TagName))
                    return false;

                // 타입 필터
                if (typeFilter.Contains("Structure") && !entry.IsStructure) return false;
                if (typeFilter.Contains("Boolean") && !entry.DataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase)) return false;

                // 값 필터 (Boolean 타입에만 적용)
                if (valueFilter.Contains("ON") && !entry.IsOn) return false;
                if (valueFilter.Contains("OFF") && entry.IsOn) return false;

                // 시간대 필터
                if (!hourFilter.StartsWith("전체"))
                {
                    var filterHour = hourFilter.Split('시')[0];
                    if (entry.Hour != filterHour) return false;
                }

                // 검색 필터
                if (!string.IsNullOrEmpty(searchText))
                {
                    return MatchesSearchCriteria(entry, searchText);
                }

                return true;
            };

            UpdateStatus();
        }

        private bool MatchesSearchCriteria(FLLogEntry entry, string searchText)
        {
            // OR 조건 (쉼표)
            var orGroups = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var orGroup in orGroups)
            {
                // AND 조건 (플러스)
                var andTerms = orGroup.Split('+', StringSplitOptions.RemoveEmptyEntries);
                bool allMatch = true;

                foreach (var term in andTerms)
                {
                    var t = term.Trim();
                    if (string.IsNullOrEmpty(t)) continue;

                    bool match = entry.TagName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 entry.Value.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 entry.ModuleName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 entry.Level.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 entry.DataType.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                 entry.Fields.Any(f => f.Key.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                                       f.Value.Contains(t, StringComparison.OrdinalIgnoreCase));

                    if (!match)
                    {
                        allMatch = false;
                        break;
                    }
                }

                if (allMatch) return true;
            }

            return false;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyFilters();
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
            ApplyFilters();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void CboValueFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitialized)
                ApplyFilters();
        }


        private void CboHourFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitialized)
                ApplyFilters();
        }

        #endregion

        #region 태그 필터

        /// <summary>
        /// 태그 콤보박스 열릴 때 - 발견된 태그명 목록 갱신
        /// </summary>
        private void CboTagFilter_DropDownOpened(object? sender, EventArgs e)
        {
            // 현재 로그에서 발견된 태그명들로 목록 갱신
            var newTagNames = logEntries.Select(x => x.TagName).Distinct().OrderBy(x => x).ToList();
            
            foreach (var tagName in newTagNames)
            {
                if (!discoveredTagNames.Contains(tagName))
                {
                    discoveredTagNames.Add(tagName);
                    tagFilterItems.Add(new BizFilterItem 
                    { 
                        Name = tagName, 
                        IsSelected = selectedTagNames.Count == 0 || selectedTagNames.Contains(tagName)
                    });
                }
            }
        }

        /// <summary>
        /// 태그 체크박스 클릭
        /// </summary>
        private void TagFilterCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateTagFilterSelection();
        }

        /// <summary>
        /// 태그 전체 선택
        /// </summary>
        private void BtnTagSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tagFilterItems)
            {
                item.IsSelected = true;
            }
            UpdateTagFilterSelection();
        }

        /// <summary>
        /// 태그 전체 해제
        /// </summary>
        private void BtnTagDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tagFilterItems)
            {
                item.IsSelected = false;
            }
            UpdateTagFilterSelection();
        }

        /// <summary>
        /// 태그 필터 선택 상태 업데이트
        /// </summary>
        private void UpdateTagFilterSelection()
        {
            // 선택된 태그명 업데이트
            selectedTagNames.Clear();
            foreach (var item in tagFilterItems.Where(x => x.IsSelected))
            {
                selectedTagNames.Add(item.Name);
            }

            // 콤보박스 텍스트 업데이트
            if (tagFilterItems.Count == 0)
            {
                cboTagFilter.Text = "전체";
            }
            else if (selectedTagNames.Count == 0)
            {
                cboTagFilter.Text = "선택 없음";
            }
            else if (selectedTagNames.Count == tagFilterItems.Count)
            {
                cboTagFilter.Text = "전체";
            }
            else if (selectedTagNames.Count <= 2)
            {
                cboTagFilter.Text = string.Join(", ", selectedTagNames);
            }
            else
            {
                cboTagFilter.Text = $"{selectedTagNames.Count}개 선택";
            }

            // 필터 적용
            ApplyFilters();
        }

        private void CboTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitialized)
                ApplyFilters();
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
        /// 지정한 시간으로 이동 (필터링된 데이터에서 검색)
        /// </summary>
        private void JumpToTime()
        {
            if (currentDataGrid == null || currentLogView == null) return;

            // 필터링된 데이터 가져오기 (시간순 정렬, 같은 시간일 때는 행 번호순)
            var filteredEntries = currentLogView.Cast<FLLogEntry>()
                .OrderBy(e => e.Timestamp)
                .ThenBy(e => e.RowNumber)
                .ToList();
            if (filteredEntries.Count == 0) return;

            string timeText = txtJumpTime.Text.Trim();
            if (string.IsNullOrEmpty(timeText)) return;

            // 시간 파싱
            TimeSpan targetTime;
            bool isMinuteOnlySearch = false; // 분 단위 검색 여부
            
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
                    isMinuteOnlySearch = true; // 분 단위로 검색
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
                MessageBox.Show("시간 형식이 올바르지 않습니다.\n예: 09:30, 14:00, 9, 11:52:17", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 필터링된 데이터에서 해당 시간 이후의 첫 번째 로그 찾기
            FLLogEntry? targetEntry = null;
            
            if (isMinuteOnlySearch)
            {
                // 분 단위 검색: HH:mm:00 ~ HH:mm:59 범위에서 찾기
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
                // 정확한 시간 이후 검색
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

        #region UI 이벤트

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            logEntries.Clear();
            loadedFiles.Clear();
            loadedHours.Clear();
            tabLogEntries.Clear();
            tabLogViews.Clear();
            tabControlLogs.Items.Clear();
            cboHourFilter.Items.Clear();
            cboHourFilter.Items.Add(new ComboBoxItem { Content = "전체", IsSelected = true });
            txtFileCount.Text = "파일: 0개";
            UpdateStatus();
        }

        private void BtnAutoScroll_Click(object sender, RoutedEventArgs e)
        {
            isAutoScrollEnabled = !isAutoScrollEnabled;
            btnAutoScroll.Background = isAutoScrollEnabled
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                : new SolidColorBrush(Color.FromRgb(158, 158, 158));
            btnAutoScroll.Content = isAutoScrollEnabled ? "⬇ 자동스크롤" : "⬇ 자동스크롤 OFF";
        }

        private void BtnRealTimeWatch_Click(object sender, RoutedEventArgs e)
        {
            isRealTimeWatchEnabled = !isRealTimeWatchEnabled;
            
            if (isRealTimeWatchEnabled)
            {
                StartFileWatcher();
                btnRealTimeWatch.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                btnRealTimeWatch.Content = "▶️ 실시간";
                txtStatus.Text = "실시간 감지 재개";
            }
            else
            {
                StopFileWatcher();
                btnRealTimeWatch.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                btnRealTimeWatch.Content = "⏸️ 일시정지";
                txtStatus.Text = "실시간 감지 일시정지";
            }
        }

        private async void BtnRefreshCurrent_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCurrentHourFiles();
        }

        private void BtnAutoFit_Click(object sender, RoutedEventArgs e)
        {
            ApplyAutoFit();
        }

        public void ApplyAutoFit()
        {
            if (currentDataGrid == null) return;

            foreach (var column in currentDataGrid.Columns)
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }

            currentDataGrid.UpdateLayout();

            foreach (var column in currentDataGrid.Columns)
            {
                var actualWidth = column.ActualWidth;
                column.Width = new DataGridLength(actualWidth);
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid?.SelectedItem is FLLogEntry entry)
            {
                var popup = new LogDetailPopup();
                popup.SetFLLogContent(entry);
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
                if (sender is DataGrid dataGrid && dataGrid.CurrentCell.Column != null && dataGrid.SelectedItem is FLLogEntry entry)
                {
                    var column = dataGrid.CurrentCell.Column;
                    string? cellValue = null;

                    if (column.Header is string header)
                    {
                        cellValue = header switch
                        {
                            "#" => entry.RowNumber.ToString(),
                            "시간대" => entry.Hour,
                            "시간" => entry.TimeString,
                            "태그명" => entry.TagName,
                            "태그설명" => entry.TagDescription,
                            "타입" => entry.DataType,
                            "Boolean" => entry.Value,
                            "Structure 값" => entry.Value,
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

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            ExportToExcel();
        }

        private void ExportToExcel()
        {
            try
            {
                if (currentLogView == null) return;

                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                    FileName = $"FL_Log_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() != true) return;

                var items = currentLogView.Cast<FLLogEntry>().ToList();

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("F/L 로그");

                // 헤더
                var headers = new[] { "#", "시간", "레벨", "모듈", "태그명", "타입", "값", "시간대", "파일" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cells[1, i + 1].Value = headers[i];
                    worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                    worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(156, 39, 176));
                    worksheet.Cells[1, i + 1].Style.Font.Color.SetColor(System.Drawing.Color.White);
                }

                // 데이터
                for (int i = 0; i < items.Count; i++)
                {
                    var entry = items[i];
                    worksheet.Cells[i + 2, 1].Value = entry.RowNumber;
                    worksheet.Cells[i + 2, 2].Value = entry.TimeString;
                    worksheet.Cells[i + 2, 3].Value = entry.Level;
                    worksheet.Cells[i + 2, 4].Value = entry.ShortModuleName;
                    worksheet.Cells[i + 2, 5].Value = entry.TagName;
                    worksheet.Cells[i + 2, 6].Value = entry.DataType;
                    worksheet.Cells[i + 2, 7].Value = entry.Value;
                    worksheet.Cells[i + 2, 8].Value = $"{entry.Hour}시";
                    worksheet.Cells[i + 2, 9].Value = entry.SourceFile;
                }

                worksheet.Cells.AutoFitColumns();

                File.WriteAllBytes(saveDialog.FileName, package.GetAsByteArray());
                MessageBox.Show($"엑셀 파일이 저장되었습니다.\n{saveDialog.FileName}", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 저장 중 오류 발생:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 폰트 크기

        private void BtnFontMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int size) && size > 8)
            {
                size--;
                txtFontSize.Text = size.ToString();
                ApplyFontSizeToAllTabs(size);
                SaveFontSize(size);
            }
        }

        private void BtnFontPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int size) && size < 20)
            {
                size++;
                txtFontSize.Text = size.ToString();
                ApplyFontSizeToAllTabs(size);
                SaveFontSize(size);
            }
        }

        private void ApplyFontSizeToAllTabs(int size)
        {
            foreach (TabItem tabItem in tabControlLogs.Items)
            {
                if (tabItem.Content is DataGrid dataGrid)
                {
                    dataGrid.FontSize = size;
                }
            }
        }

        private void LoadSavedFontSize()
        {
            try
            {
                var settings = AppSettingsManager.Settings;
                if (settings.FLLogFontSize > 0)
                {
                    txtFontSize.Text = settings.FLLogFontSize.ToString();
                }
            }
            catch { }
        }

        private void SaveFontSize(int size)
        {
            try
            {
                AppSettingsManager.Settings.FLLogFontSize = size;
                AppSettingsManager.SaveCurrent();
            }
            catch { }
        }

        #endregion

        #region 상태 표시

        private void UpdateStatus()
        {
            var total = logEntries.Count;
            var filtered = currentLogView?.Cast<object>().Count() ?? 0;

            // 현재 탭 이름
            var tabName = "";
            if (tabControlLogs.SelectedItem is TabItem tabItem && tabItem.Tag is FLTabConfig config)
            {
                tabName = config.Name;
            }

            txtCount.Text = $" | 전체: {total:N0}건";
            txtFilteredCount.Text = $" | {tabName}: {filtered:N0}건";
            txtStatus.Text = isInitialized ? "로드 완료" : "준비";

            // 탭 헤더 업데이트
            UpdateTabHeaders();
        }

        private void UpdateTabHeaders()
        {
            foreach (TabItem tabItem in tabControlLogs.Items)
            {
                if (tabItem.Tag is FLTabConfig config)
                {
                    if (tabLogEntries.TryGetValue(config.Name, out var entries))
                    {
                        var view = tabLogViews.TryGetValue(config.Name, out var v) ? v : null;
                        var count = view?.Cast<object>().Count() ?? entries.Count;
                        tabItem.Header = $"{config.Name} ({count:N0})";
                    }
                }
            }
        }

        private void ShowLoadingOverlay(bool show)
        {
            loadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateLoadingStatus(string status)
        {
            txtLoadingStatus.Text = status;
        }

        private double GetCurrentFontSize()
        {
            if (int.TryParse(txtFontSize.Text, out int size) && size > 0)
                return size;
            return 11;
        }

        #endregion

        #region 현재 시간 파일 갱신

        /// <summary>
        /// 현재 시간대 파일 강제 갱신 (복사로 flush 트리거 → 복사본 읽기)
        /// </summary>
        private async Task RefreshCurrentHourFiles()
        {
            if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory))
            {
                MessageBox.Show("로그 디렉토리가 설정되지 않았습니다.\nF/L 로그를 먼저 로드해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var currentHour = DateTime.Now.Hour.ToString("D2");
                var currentMonth = DateTime.Now.Month.ToString("D2");
                var currentDay = DateTime.Now.Day.ToString("D2");

                // 현재 시간대 파일 찾기 (_MMDDYY.log)
                var targetFiles = Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                    {
                        var fileName = Path.GetFileName(f);
                        if (fileName.StartsWith("LGE", StringComparison.OrdinalIgnoreCase))
                            return false;

                        var match = FileNamePattern.Match(fileName);
                        if (!match.Success) return false;

                        var fileMonth = match.Groups[1].Value;
                        var fileDay = match.Groups[2].Value;
                        var fileHour = match.Groups[3].Value;

                        return fileMonth == currentMonth && fileDay == currentDay && fileHour == currentHour;
                    })
                    .ToList();

                if (targetFiles.Count == 0)
                {
                    MessageBox.Show($"현재 시간대({currentHour}시) 파일을 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                txtStatus.Text = $"📥 현재시간({currentHour}시) 파일 갱신 중...";

                // 로그 디렉토리 안에 Temp 폴더 생성
                var tempFolder = Path.Combine(logDirectory, "Temp");
                Directory.CreateDirectory(tempFolder);

                var copiedFiles = new List<string>();

                try
                {
                    foreach (var file in targetFiles)
                    {
                        // Temp 폴더에 복사 (이 과정에서 OS가 버퍼 flush!)
                        var fileName = Path.GetFileName(file);
                        var tempPath = Path.Combine(tempFolder, fileName);
                        
                        File.Copy(file, tempPath, true);
                        copiedFiles.Add(tempPath);
                        
                        System.Diagnostics.Debug.WriteLine($"✅ 파일 복사로 flush 트리거: {fileName} → Temp\\{fileName}");
                    }

                    // 약간의 지연 (flush 완료 대기)
                    await Task.Delay(100);

                    // 복사본 파일들 재로드
                    await ReloadSpecificFiles(copiedFiles);

                    MessageBox.Show($"현재 시간대({currentHour}시) 로그 {targetFiles.Count}개 파일을 갱신했습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                finally
                {
                    // Temp 폴더의 복사본 파일들 삭제
                    foreach (var tempFile in copiedFiles)
                    {
                        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"현재 시간 파일 갱신 중 오류:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"❌ 현재 시간 파일 갱신 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 특정 파일들 재로드 (최적화: 증분 업데이트)
        /// </summary>
        private async Task ReloadSpecificFiles(List<string> filePaths)
        {
            var fileNames = filePaths.Select(Path.GetFileName).ToHashSet();
            
            // 1단계: 해당 파일의 기존 로그를 빠르게 제거
            var entriesToRemove = logEntries.Where(e => fileNames.Contains(e.SourceFile)).ToList();
            
            foreach (var entry in entriesToRemove)
            {
                logEntries.Remove(entry);
            }

            // 탭별로도 제거
            foreach (var tabEntries in tabLogEntries.Values)
            {
                var tabEntriesToRemove = tabEntries.Where(e => fileNames.Contains(e.SourceFile)).ToList();
                foreach (var entry in tabEntriesToRemove)
                {
                    tabEntries.Remove(entry);
                }
            }

            // 2단계: 새 로그 파싱
            var reloadedEntries = new List<FLLogEntry>();
            foreach (var file in filePaths)
            {
                var newEntries = await Task.Run(() => ParseFLLogFile(file));
                reloadedEntries.AddRange(newEntries);
                System.Diagnostics.Debug.WriteLine($"📂 재로드: {Path.GetFileName(file)} → {newEntries.Count}개");
            }

            if (reloadedEntries.Count == 0)
            {
                txtStatus.Text = "재로드 완료 (변화 없음)";
                UpdateStatus();
                return;
            }

            // 3단계: 프리셋에서 태그 설명 적용
            var tagDescriptions = FLPresetManager.GetTagDescriptions();
            foreach (var entry in reloadedEntries)
            {
                if (tagDescriptions.TryGetValue(entry.TagName, out var desc))
                {
                    entry.TagDescription = desc;
                }
            }

            // 4단계: 정렬된 위치에 삽입 (이미 시간순 정렬되어 있음)
            var allLogsList = logEntries.ToList();
            allLogsList.AddRange(reloadedEntries);
            allLogsList = allLogsList.OrderBy(e => e.Timestamp).ToList();

            // 5단계: ObservableCollection 업데이트 (한번에)
            logEntries.Clear();
            for (int i = 0; i < allLogsList.Count; i++)
            {
                allLogsList[i].RowNumber = i + 1;
                logEntries.Add(allLogsList[i]);
            }

            // 6단계: 탭별 데이터 재구성 (새로 추가된 것만)
            var flSettings = FLPresetManager.CurrentSettings;
            foreach (var entry in reloadedEntries)
            {
                foreach (var tabConfig in flSettings.TabSettings?.Tabs ?? new List<FLTabConfig>())
                {
                    if (tabConfig.IsEnabled && IsEntryMatchesTab(entry, tabConfig))
                    {
                        if (tabLogEntries.TryGetValue(tabConfig.Name, out var tabEntries))
                        {
                            tabEntries.Add(entry);
                        }
                    }
                }
            }

            UpdateStatus();
            txtStatus.Text = $"✅ 재로드 완료: +{reloadedEntries.Count}개";

            // 자동 스크롤
            if (isAutoScrollEnabled && currentDataGrid != null && currentLogView != null)
            {
                var items = currentLogView.Cast<object>().ToList();
                if (items.Count > 0)
                {
                    currentDataGrid.ScrollIntoView(items.Last());
                }
            }
        }

        #endregion

        #region 실시간 감지 (FileSystemWatcher)

        private void StartFileWatcher()
        {
            if (string.IsNullOrEmpty(logDirectory) || !Directory.Exists(logDirectory))
                return;

            try
            {
                StopFileWatcher();

                fileWatcher = new FileSystemWatcher(logDirectory)
                {
                    Filter = "*.log",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                fileWatcher.Changed += OnFileChanged;
                fileWatcher.Created += OnFileChanged;

                // Debounce 타이머 설정
                watcherDebounceTimer = new System.Timers.Timer(500);
                watcherDebounceTimer.Elapsed += OnWatcherDebounceElapsed;
                watcherDebounceTimer.AutoReset = false;

                System.Diagnostics.Debug.WriteLine($"📡 F/L 로그 실시간 감지 시작: {logDirectory}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ FileWatcher 시작 실패: {ex.Message}");
            }
        }

        private void StopFileWatcher()
        {
            if (fileWatcher != null)
            {
                fileWatcher.EnableRaisingEvents = false;
                fileWatcher.Dispose();
                fileWatcher = null;
            }

            if (watcherDebounceTimer != null)
            {
                watcherDebounceTimer.Stop();
                watcherDebounceTimer.Dispose();
                watcherDebounceTimer = null;
            }

            pendingWatcherFiles.Clear();
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // F/L 로그 파일 패턴 확인
            if (!FileNamePattern.IsMatch(Path.GetFileName(e.FullPath)))
                return;

            lock (pendingWatcherFiles)
            {
                pendingWatcherFiles.Add(e.FullPath);
            }

            // Debounce 타이머 재시작
            watcherDebounceTimer?.Stop();
            watcherDebounceTimer?.Start();
        }

        private void OnWatcherDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // 일시정지 상태면 무시
            if (!isRealTimeWatchEnabled)
                return;

            List<string> filesToProcess;
            lock (pendingWatcherFiles)
            {
                filesToProcess = pendingWatcherFiles.ToList();
                pendingWatcherFiles.Clear();
            }

            if (filesToProcess.Count == 0) return;

            Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    foreach (var file in filesToProcess)
                    {
                        if (!File.Exists(file)) continue;

                        var newEntries = await Task.Run(() => ParseFLLogFile(file));
                        
                        // 프리셋에서 태그 설명 적용
                        var tagDescriptions = FLPresetManager.GetTagDescriptions();
                        
                        foreach (var entry in newEntries)
                        {
                            // 중복 체크 (같은 시간, 같은 태그)
                            if (logEntries.Any(e => e.Timestamp == entry.Timestamp && e.TagName == entry.TagName))
                                continue;

                            entry.RowNumber = logEntries.Count + 1;
                            if (tagDescriptions.TryGetValue(entry.TagName, out var desc))
                            {
                                entry.TagDescription = desc;
                            }

                            logEntries.Add(entry);

                            // 각 탭에도 추가
                            var flSettings = FLPresetManager.CurrentSettings;
                            foreach (var tabConfig in flSettings.TabSettings?.Tabs ?? new List<FLTabConfig>())
                            {
                                if (tabConfig.IsEnabled && IsEntryMatchesTab(entry, tabConfig))
                                {
                                    if (tabLogEntries.TryGetValue(tabConfig.Name, out var tabEntries))
                                    {
                                        tabEntries.Add(entry);
                                    }
                                }
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"📡 F/L 실시간 업데이트: {newEntries.Count}개 추가");
                    }

                    UpdateStatus();

                    // 자동 스크롤
                    if (isAutoScrollEnabled && currentDataGrid != null && currentLogView != null)
                    {
                        var items = currentLogView.Cast<object>().ToList();
                        if (items.Count > 0)
                        {
                            currentDataGrid.ScrollIntoView(items.Last());
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 실시간 업데이트 실패: {ex.Message}");
                }
            });
        }

        #endregion
    }
}
