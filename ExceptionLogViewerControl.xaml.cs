using FACTOVA_MessageLogViewer.Models;
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

namespace FACTOVA_MessageLogViewer
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
        private System.Threading.Timer? searchDebounceTimer;
        private readonly object fileLock = new object();
        private bool isReading = false;
        private bool isLoadingBatch = false;

        private string currentLogDirectory = "";
        private bool isDefaultFolder = true;
        private bool isAutoScrollEnabled = true;

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
            searchDebounceTimer?.Dispose();
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

            if (string.IsNullOrEmpty(currentLogFile) || !File.Exists(currentLogFile))
            {
                txtLogFolder.Text = "(파일 없음)";
                return;
            }

            txtLogFolder.Text = $"({Path.GetFileName(currentLogFile)})";

            LoadLogs();
            StartFileWatcher();
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
                    
                    if (isAutoScrollEnabled && logEntries.Count > 0)
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            dataGrid.ScrollIntoView(logEntries[^1]);
                        }, System.Windows.Threading.DispatcherPriority.Background);
                    }
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

                // 예외 타입 추출 (예: System.NullReferenceException:)
                var exceptionTypeMatch = Regex.Match(entryText, @"([A-Za-z\.]+Exception):");
                string exceptionType = exceptionTypeMatch.Success ? exceptionTypeMatch.Groups[1].Value : "Exception";

                // 메시지 추출 (예외 타입 다음 줄)
                string message = "";
                var lines = entryText.Split('\n');
                if (lines.Length > 0)
                {
                    // 첫 줄에서 예외 타입 이후의 메시지 추출
                    var firstLine = lines[0];
                    var colonIndex = firstLine.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < firstLine.Length - 1)
                    {
                        // 두 번째 콜론 이후가 메시지
                        var afterType = firstLine.Substring(colonIndex + 1);
                        var secondColon = afterType.IndexOf(':');
                        if (secondColon > 0)
                        {
                            message = afterType.Substring(secondColon + 1).Trim();
                        }
                        else
                        {
                            message = afterType.Trim();
                        }
                    }
                }

                // 소스 추출 (at 이후의 첫 번째 줄)
                string source = "";
                var atMatch = Regex.Match(entryText, @"at\s+([^\r\n]+)");
                if (atMatch.Success)
                {
                    source = atMatch.Groups[1].Value.Trim();
                    if (source.Length > 50)
                        source = "..." + source.Substring(source.Length - 50);
                }

                return new ExceptionLogEntry
                {
                    Timestamp = timestamp,
                    ExceptionType = exceptionType,
                    Message = message,
                    Source = source,
                    StackTrace = entryText,
                    RawData = entryText.Trim()
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
            searchDebounceTimer?.Dispose();
            searchDebounceTimer = new System.Threading.Timer(_ =>
            {
                Dispatcher.BeginInvoke(() => ApplySearch());
            }, null, 300, System.Threading.Timeout.Infinite);
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

            var orConditions = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var orCondition in orConditions)
            {
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

                if (allMatch)
                    return true;
            }

            return false;
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
            if (logEntries.Count == 0) return;

            string timeText = txtJumpTime.Text.Trim();
            if (string.IsNullOrEmpty(timeText)) return;

            TimeSpan targetTime;
            if (timeText.Length <= 2 && int.TryParse(timeText, out int hourOnly))
            {
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
                MessageBox.Show("시간 형식이 올바르지 않습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExceptionLogEntry? targetEntry = null;
            for (int i = 0; i < logEntries.Count; i++)
            {
                if (logEntries[i].Timestamp.TimeOfDay >= targetTime)
                {
                    targetEntry = logEntries[i];
                    break;
                }
            }

            if (targetEntry == null)
            {
                MessageBox.Show($"{targetTime:hh\\:mm} 이후의 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
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
            txtCount.Text = $"| 총 {total:N0}건";
            txtFile.Text = Path.GetFileName(currentLogFile);
            txtPausedCount.Text = isPaused && pausedBuffer.Count > 0 ? $"(대기: {pausedBuffer.Count}건)" : "";
            txtStatus.Text = isPaused ? "⏸ 일시정지" : "▶ 감시 중";
            txtMode.Text = loadMode switch
            {
                LogLoadMode.NewOnly => "📍 실행 이후 로그만",
                LogLoadMode.Recent => $"📚 최근 {recentCount}개",
                LogLoadMode.All => "📖 전체 로그",
                _ => ""
            };
        }

        #endregion
    }
}
