using FACTOVA_MessageLogViewer.Helpers;
using FACTOVA_MessageLogViewer.Models;
using FACTOVA_MessageLogViewer.Popup;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using OfficeOpenXml;

namespace FACTOVA_MessageLogViewer.Views
{
    public partial class ExceptionLogViewerControl : UserControl
    {
        private ObservableCollection<ExceptionLogEntry> logEntries = new();
        private System.ComponentModel.ICollectionView logView = null!;

        private FileSystemWatcher? fileWatcher;
        private string logDirectory = "";
        private string currentLogFile = "";
        private long lastPosition = 0;

        private bool isPaused = false;
        private List<ExceptionLogEntry> pausedBuffer = new();

        private LogLoadMode loadMode;
        private int recentCount;

        private StringBuilder multiLineBuffer = new StringBuilder();
        
        // EXCEPTION 로그 시작 패턴: [MM-DD-YYYY HH:mm:ss.fff]
        private static readonly Regex ExceptionLogStartPattern = new Regex(
            @"\[(\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)\]", 
            RegexOptions.Compiled);

        private System.Threading.Timer? debounceTimer;
        private readonly object fileLock = new object();
        private bool isReading = false;
        private bool isLoadingBatch = false;

        private string currentLogDirectory = "";
        private bool isDefaultFolder = true;
        private bool isAutoScrollEnabled = true;
        private bool enableRealTimeWatch = true;

        private TimeSpan filterStartTime = TimeSpan.Zero;
        private TimeSpan filterEndTime = new TimeSpan(23, 59, 59);

        public ExceptionLogViewerControl()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            logView = CollectionViewSource.GetDefaultView(logEntries);
            dataGrid.ItemsSource = logView;
        }

        public void Cleanup()
        {
            StopFileWatcher();
            debounceTimer?.Dispose();
        }

        /// <summary>
        /// 설정으로 초기화 (MainWindow에서 호출)
        /// </summary>
        public void Initialize(LogViewerSettings settings)
        {
            StopFileWatcher();
            
            logEntries.Clear();

            currentLogFile = settings.LogFilePath;
            logDirectory = settings.LogDirectory;
            loadMode = settings.LoadMode;
            recentCount = settings.RecentCount;
            filterStartTime = settings.FilterStartTime;
            filterEndTime = settings.FilterEndTime;
            currentLogDirectory = settings.LogDirectory;
            isDefaultFolder = settings.IsDefaultFolder;
            enableRealTimeWatch = settings.EnableRealTimeWatch;

            if (string.IsNullOrEmpty(currentLogFile) || !File.Exists(currentLogFile))
            {
                txtLogFolder.Text = "(파일 없음)";
                return;
            }

            txtLogFolder.Text = $"({Path.GetFileName(currentLogFile)})";

            LoadLogs();
            
            // 실시간 감지가 활성화된 경우에만 파일 감시 시작
            if (enableRealTimeWatch)
            {
                StartFileWatcher();
            }
            
            UpdateStatus();

            // Auto Fit 자동 적용
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyAutoFitInternal();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            System.Diagnostics.Debug.WriteLine($"⚠️ EXCEPTION 로그 초기화 완료: {currentLogFile}");
        }

        #region 로그 파싱

        private void LoadLogs()
        {
            if (!File.Exists(currentLogFile)) return;

            switch (loadMode)
            {
                case LogLoadMode.NewOnly:
                    lastPosition = new FileInfo(currentLogFile).Length;
                    break;
                case LogLoadMode.Recent:
                case LogLoadMode.All:
                    isLoadingBatch = true;
                    
                    var content = File.ReadAllText(currentLogFile, Encoding.UTF8);
                    var entries = ParseExceptionLogEntries(content);
                    
                    if (loadMode == LogLoadMode.Recent)
                    {
                        entries = entries.TakeLast(recentCount).ToList();
                    }
                    else
                    {
                        entries = entries.Where(e => 
                            e.Timestamp.TimeOfDay >= filterStartTime && 
                            e.Timestamp.TimeOfDay <= filterEndTime).ToList();
                    }

                    int rowNum = 1;
                    foreach (var entry in entries)
                    {
                        entry.RowNumber = rowNum++;
                        logEntries.Add(entry);
                    }

                    lastPosition = new FileInfo(currentLogFile).Length;
                    isLoadingBatch = false;
                    break;
            }
        }

        private List<ExceptionLogEntry> ParseExceptionLogEntries(string content)
        {
            var entries = new List<ExceptionLogEntry>();
            var matches = ExceptionLogStartPattern.Matches(content);

            for (int i = 0; i < matches.Count; i++)
            {
                int startIndex = matches[i].Index;
                int endIndex = (i + 1 < matches.Count) ? matches[i + 1].Index : content.Length;
                string entryText = content.Substring(startIndex, endIndex - startIndex);
                var entry = ParseSingleExceptionEntry(entryText, matches[i]);
                if (entry != null) entries.Add(entry);
            }
            
            return entries;
        }

        // 비즈명 추출 패턴: [BR_SFC_xxx] 또는 ExecuteServiceSync():[BR_SFC_xxx]
        private static readonly Regex BizNamePattern = new Regex(
            @"\[?(BR_[A-Za-z0-9_]+)\]?",
            RegexOptions.Compiled);

        // XML 태그 값 추출 패턴: <TAG>VALUE</TAG>
        private static readonly Regex XmlTagPattern = new Regex(
            @"<([A-Za-z_][A-Za-z0-9_]*)>([^<]*)</\1>",
            RegexOptions.Compiled);

        private ExceptionLogEntry? ParseSingleExceptionEntry(string entryText, Match headerMatch)
        {
            try
            {
                string timestampStr = headerMatch.Groups[1].Value;

                DateTime.TryParseExact(timestampStr, new[] { 
                    "MM-dd-yyyy HH:mm:ss.fff",
                    "MM-dd-yyyy HH:mm:ss"
                },
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out var timestamp);

                // 예외 타입 추출 - 간단하게 "Exception"으로 표시
                string exceptionType = "Exception";
                
                // 비즈명 추출 (BR_SFC_xxx 패턴)
                string bizName = "";
                var bizMatch = BizNamePattern.Match(entryText);
                if (bizMatch.Success)
                {
                    bizName = bizMatch.Groups[1].Value;
                }

                // XML 데이터 파싱 (<NewDataSet> ~ </NewDataSet>)
                var fields = new Dictionary<string, string>();
                string message = "";
                
                int newDataSetStart = entryText.IndexOf("<NewDataSet>", StringComparison.OrdinalIgnoreCase);
                int newDataSetEnd = entryText.IndexOf("</NewDataSet>", StringComparison.OrdinalIgnoreCase);
                
                if (newDataSetStart >= 0 && newDataSetEnd > newDataSetStart)
                {
                    // XML 영역 추출
                    string xmlSection = entryText.Substring(newDataSetStart, newDataSetEnd - newDataSetStart + "</NewDataSet>".Length);
                    
                    // XML 태그에서 필드 추출
                    var tagMatches = XmlTagPattern.Matches(xmlSection);
                    foreach (Match tm in tagMatches)
                    {
                        string tagName = tm.Groups[1].Value;
                        string tagValue = tm.Groups[2].Value.Trim();
                        
                        // 컨테이너 태그 제외 (IN_DATA, OUT_DATA 등)
                        if (!string.IsNullOrEmpty(tagValue) && 
                            !tagName.EndsWith("_DATA") && 
                            !tagName.Equals("NewDataSet", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!fields.ContainsKey(tagName))
                            {
                                fields[tagName] = tagValue;
                            }
                        }
                    }
                    
                    // 메시지: </NewDataSet> 이후 내용에서 에러 메시지 추출
                    string afterXml = entryText.Substring(newDataSetEnd + "</NewDataSet>".Length).Trim();
                    
                    // 첫 줄에서 에러 메시지 추출 (: 이후, 위치: 이전)
                    var errorLines = afterXml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (errorLines.Length > 0)
                    {
                        message = errorLines[0].Trim();
                        // 앞의 콜론 제거
                        if (message.StartsWith(":"))
                            message = message.Substring(1).Trim();
                    }
                }
                else
                {
                    // XML이 없는 경우 - 첫 줄에서 타임스탬프 이후 전체
                    var lines = entryText.Split('\n');
                    if (lines.Length > 0)
                    {
                        var firstLine = lines[0].Trim();
                        var afterTimestamp = ExceptionLogStartPattern.Replace(firstLine, "").Trim();
                        message = afterTimestamp;
                    }
                }

                // 소스 추출 (위치: 이후의 첫 번째 줄)
                string source = "";
                var atMatch = Regex.Match(entryText, @"위치:\s*([^\r\n]+)");
                if (!atMatch.Success)
                {
                    atMatch = Regex.Match(entryText, @"at\s+([^\r\n]+)");
                }
                if (atMatch.Success)
                {
                    source = atMatch.Groups[1].Value.Trim();
                    if (source.Length > 80)
                        source = "..." + source.Substring(source.Length - 80);
                }

                return new ExceptionLogEntry
                {
                    Timestamp = timestamp,
                    ExceptionType = exceptionType,
                    BizName = bizName,
                    Message = message,
                    Source = source,
                    StackTrace = entryText,
                    RawData = entryText.Trim(),
                    Fields = fields
                };
            }
            catch { return null; }
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
            if (isReading || isPaused) return;
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

                foreach (var entry in ParseExceptionLogEntries(newContent))
                {
                    entry.RowNumber = logEntries.Count + 1;
                    logEntries.Add(entry);
                }

                lastPosition = fileInfo.Length;
                
                UpdateStatus();
                
                if (isAutoScrollEnabled && logEntries.Count > 0)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        dataGrid.ScrollIntoView(logEntries[^1]);
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
            finally { isReading = false; }
        }

        #endregion

        #region UI 이벤트

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            isPaused = !isPaused;
            btnPause.Content = isPaused ? "▶ 재개" : "⏸ 일시정지";
            UpdateStatus();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            logEntries.Clear();
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
            ApplyAutoFitInternal();
        }

        public void ApplyAutoFit()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyAutoFitInternal();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ApplyAutoFitInternal()
        {
            try
            {
                foreach (var column in dataGrid.Columns)
                {
                    column.Width = DataGridLength.Auto;
                }
                dataGrid.UpdateLayout();
                foreach (var column in dataGrid.Columns)
                {
                    double actualWidth = column.ActualWidth;
                    if (actualWidth > 0)
                    {
                        column.Width = new DataGridLength(actualWidth + 15);
                    }
                }
            }
            catch { }
        }

        private void BtnFontMinus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int s) && s > 8)
            {
                txtFontSize.Text = (--s).ToString();
                dataGrid.FontSize = s;
            }
        }

        private void BtnFontPlus_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtFontSize.Text, out int s) && s < 20)
            {
                txtFontSize.Text = (++s).ToString();
                dataGrid.FontSize = s;
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e) 
        { 
            if (e.Key == Key.Enter) 
            {
                ApplySearch();
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
            ApplySearch();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            ApplySearch();
        }

        private void ApplySearch()
        {
            string searchText = txtSearch.Text.Trim();

            if (string.IsNullOrEmpty(searchText))
            {
                logView.Filter = null;
            }
            else
            {
                logView.Filter = obj =>
                {
                    if (obj is not ExceptionLogEntry entry) return false;
                    return MatchesComplexSearch(entry, searchText);
                };
            }
            
            logView.Refresh();
            UpdateStatus();
        }

        private bool MatchesComplexSearch(ExceptionLogEntry entry, string searchText)
        {
            string searchTarget = $"{entry.ExceptionType} {entry.Message} {entry.Source} {entry.StackTrace}";
            return SearchHelper.MatchesComplexSearch(searchText, searchTarget);
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dataGrid.SelectedItem is ExceptionLogEntry entry)
            {
                var popup = new LogDetailPopup();
                popup.SetExceptionLogContent(entry);
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
                if (dataGrid.CurrentCell.Column != null && dataGrid.SelectedItem is ExceptionLogEntry entry)
                {
                    var column = dataGrid.CurrentCell.Column;
                    string? cellValue = null;

                    if (column.Header is string header)
                    {
                        cellValue = header switch
                        {
                            "No" => entry.RowNumber.ToString(),
                            "시간" => entry.TimeString,
                            "예외 타입" => entry.ExceptionType,
                            "메시지" => entry.Message,
                            "소스" => entry.Source,
                            "상세" => entry.Summary,
                            _ => null
                        };
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

        private void TxtJumpTime_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                JumpToTime();
                e.Handled = true;
            }
        }

        private void BtnJumpToTime_Click(object sender, RoutedEventArgs e)
        {
            JumpToTime();
        }

        private void JumpToTime()
        {
            if (logView == null) return;

            // 필터링된 데이터 가져오기 (시간순 정렬, 같은 시간일 때는 행 번호순)
            var filteredEntries = logView.Cast<ExceptionLogEntry>()
                .OrderBy(e => e.Timestamp)
                .ThenBy(e => e.RowNumber)
                .ToList();
            if (filteredEntries.Count == 0) return;

            string timeText = txtJumpTime.Text.Trim();
            if (string.IsNullOrEmpty(timeText)) return;

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

            // 필터링된 데이터에서 시간 검색
            ExceptionLogEntry? targetEntry = null;
            
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
                dataGrid.ScrollIntoView(targetEntry);
                dataGrid.SelectedItem = targetEntry;
                dataGrid.Focus();
            }
            catch { }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (logEntries.Count == 0)
            {
                MessageBox.Show("내보낼 로그가 없습니다.", "알림"); return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel|*.xlsx", FileName = $"EXCEPTION_Log_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using var pkg = new ExcelPackage();
                var sheet = pkg.Workbook.Worksheets.Add("Log");
                sheet.Cells[1, 1].Value = "No"; 
                sheet.Cells[1, 2].Value = "시간"; 
                sheet.Cells[1, 3].Value = "예외 타입";
                sheet.Cells[1, 4].Value = "메시지"; 
                sheet.Cells[1, 5].Value = "소스";

                int row = 2;
                foreach (var entry in logEntries)
                {
                    sheet.Cells[row, 1].Value = entry.RowNumber;
                    sheet.Cells[row, 2].Value = entry.TimeString;
                    sheet.Cells[row, 3].Value = entry.ExceptionType;
                    sheet.Cells[row, 4].Value = entry.Message;
                    sheet.Cells[row, 5].Value = entry.Source;
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
            int total = logEntries.Count;
            txtCount.Text = $" | 전체: {total:N0}건";
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
    }
}
