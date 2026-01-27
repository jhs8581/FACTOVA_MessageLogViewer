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

            InitializeLogManager();
            InitializeDynamicColumns();  // 동적 컬럼 생성
            StartFileWatcher();
            LoadLogs();

            UpdateModeText();
        }

        /// <summary>
        /// 설정에 따라 동적 컬럼 생성
        /// </summary>
        private void InitializeDynamicColumns()
        {
            var settings = ColumnSettingsManager.CurrentSettings;
            
            // Summary 컬럼 위치 (마지막에서 앞으로 삽입)
            int insertIndex = gridView.Columns.Count - 1;  // Summary 컬럼 앞

            foreach (var fieldConfig in settings.ColumnFields)
            {
                var column = CreateDynamicColumn(fieldConfig);
                gridView.Columns.Insert(insertIndex, column);
                insertIndex++;
            }
        }


        /// <summary>
        /// 필드 설정에 따라 GridViewColumn 생성
        /// </summary>
        private GridViewColumn CreateDynamicColumn(FieldConfig config)
        {
            // 헤더에서 언더바를 두 개로 변경 (WPF AccessKey 문제 해결)
            var headerText = config.DisplayName.Replace("_", "__");
            
            var column = new GridViewColumn
            {
                Header = headerText,
                // Width 0 이하면 Auto로 처리
                Width = config.ColumnWidth > 0 ? config.ColumnWidth : double.NaN
            };

            // DataTemplate 생성
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));

            // Fields 딕셔너리에서 값 가져오는 바인딩 (FontSize는 ListView에서 상속)
            factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"Fields[{config.FieldName}]"));

            // RETURN_CODE는 특별 처리 (색상)
            if (config.FieldName == "RETURN_CODE")
            {
                factory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                factory.SetBinding(TextBlock.ForegroundProperty, 
                    new System.Windows.Data.Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = (System.Windows.Data.IValueConverter)FindResource("ReturnCodeColorConverter")
                    });
            }
            // ERROR_CODE도 특별 처리
            else if (config.FieldName == "ERROR_CODE")
            {
                factory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                factory.SetBinding(TextBlock.ForegroundProperty,
                    new System.Windows.Data.Binding($"Fields[{config.FieldName}]")
                    {
                        Converter = (System.Windows.Data.IValueConverter)FindResource("ErrorColorConverter")
                    });
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

            logView = CollectionViewSource.GetDefaultView(displayEntries);
            logView.Filter = FilterLogEntry;

            listViewLog.ItemsSource = logView;

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
                        }
                    }
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    displayEntries.Clear();
                }

                logView?.Refresh();
                UpdateStatus();
                AutoScrollToBottom();
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
            if (listViewLog.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        listViewLog.ScrollIntoView(listViewLog.Items[listViewLog.Items.Count - 1]);
                    }
                    catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateStatus()
        {
            int displayCount = displayEntries.Count;
            int filteredCount = logView?.Cast<object>().Count() ?? displayCount;

            if (isPaused && pausedBuffer.Count > 0)
            {
                txtStatus.Text = $"로그 개수: {displayCount} (⏸ 일시정지 중, 대기: {pausedBuffer.Count})";
            }
            else if (isPaused)
            {
                txtStatus.Text = $"로그 개수: {displayCount} (⏸ 일시정지 중)";
            }
            else if (displayCount != filteredCount)
            {
                txtStatus.Text = $"로그 개수: {displayCount} (필터: {filteredCount})";
            }
            else
            {
                txtStatus.Text = $"로그 개수: {displayCount}";
            }
        }

        private bool FilterLogEntry(object item)
        {
            if (!(item is LogEntry entry))
                return false;

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
                logView?.Refresh();
                UpdateStatus();
            }
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            logView?.Refresh();
            UpdateStatus();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            logView?.Refresh();
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
            listViewLog.FontSize = size;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            fileWatcher?.Dispose();
            base.OnClosing(e);
        }
    }
}