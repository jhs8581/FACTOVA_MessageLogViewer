using FACTOVA_MessageLogViewer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class LogDetailPopup : Window
    {
        private LogEntry? _logEntry;
        private DataLogEntry? _dataLogEntry;
        private ExceptionLogEntry? _exceptionLogEntry;
        private FLLogEntry? _flLogEntry;

        public LogDetailPopup(LogEntry entry)
        {
            InitializeComponent();
            _logEntry = entry;
            LoadLogDetails();
        }

        public LogDetailPopup()
        {
            InitializeComponent();
        }

        /// <summary>
        /// DATA 로그 내용 설정
        /// </summary>
        public void SetDataLogContent(DataLogEntry entry)
        {
            _dataLogEntry = entry;
            LoadDataLogDetails();
        }

        /// <summary>
        /// EXCEPTION 로그 내용 설정
        /// </summary>
        public void SetExceptionLogContent(ExceptionLogEntry entry)
        {
            _exceptionLogEntry = entry;
            LoadExceptionLogDetails();
        }

        /// <summary>
        /// F/L 로그 내용 설정
        /// </summary>
        public void SetFLLogContent(FLLogEntry entry)
        {
            _flLogEntry = entry;
            LoadFLLogDetails();
        }

        private void LoadLogDetails()
        {
            if (_logEntry == null) return;

            // 헤더 정보
            txtTime.Text = _logEntry.TimeString;
            txtDirection.Text = _logEntry.DirectionText;
            txtMsgId.Text = _logEntry.MessageId;
            txtMatchedTab.Text = string.IsNullOrEmpty(_logEntry.MatchedTabName) ? "(없음)" : _logEntry.MatchedTabName;

            // 구분 색상
            if (_logEntry.Direction == "SEND")
            {
                txtDirection.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(21, 101, 192)); // Blue
            }
            else
            {
                txtDirection.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(46, 125, 50)); // Green
            }

            // 필드 목록
            var fieldList = _logEntry.Fields
                .OrderBy(f => f.Key)
                .Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                .ToList();
            dgFields.ItemsSource = fieldList;

            // Raw Data
            txtRawData.Text = FormatRawData(_logEntry.RawData);
        }

        private void LoadDataLogDetails()
        {
            if (_dataLogEntry == null) return;

            // 헤더 정보
            txtTime.Text = _dataLogEntry.TimeString;
            txtDirection.Text = $"실행시간: {_dataLogEntry.ExecTime}";
            txtMsgId.Text = _dataLogEntry.BizName;
            txtMatchedTab.Text = string.IsNullOrEmpty(_dataLogEntry.MatchedTabName) ? "(없음)" : _dataLogEntry.MatchedTabName;

            // DATA 로그는 주황색
            txtDirection.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 107, 0));

            // TXN_ID를 필드에 추가
            var fields = new Dictionary<string, string>(_dataLogEntry.Fields)
            {
                ["TXN_ID"] = _dataLogEntry.TxnId
            };

            // 필드 목록
            var fieldList = fields
                .OrderBy(f => f.Key)
                .Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                .ToList();
            dgFields.ItemsSource = fieldList;

            // Raw Data (XML 포맷팅)
            txtRawData.Text = FormatXml(_dataLogEntry.ParameterXml);
        }

        private void LoadExceptionLogDetails()
        {
            if (_exceptionLogEntry == null) return;

            // 헤더 정보
            txtTime.Text = _exceptionLogEntry.TimeString;
            txtDirection.Text = _exceptionLogEntry.ExceptionType;
            txtMsgId.Text = _exceptionLogEntry.Source;
            txtMatchedTab.Text = "EXCEPTION";

            // EXCEPTION 로그는 빨간색
            txtDirection.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(211, 47, 47));

            // 필드 목록
            var fields = new Dictionary<string, string>
            {
                ["예외 타입"] = _exceptionLogEntry.ExceptionType,
                ["메시지"] = _exceptionLogEntry.Message,
                ["소스"] = _exceptionLogEntry.Source
            };

            var fieldList = fields
                .Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                .ToList();
            dgFields.ItemsSource = fieldList;

            // Raw Data (스택 트레이스)
            txtRawData.Text = _exceptionLogEntry.RawData;
        }

        private void LoadFLLogDetails()
        {
            if (_flLogEntry == null) return;

            // 헤더 정보
            txtTime.Text = _flLogEntry.TimeString;
            txtDirection.Text = $"[{_flLogEntry.Level}] {_flLogEntry.Hour}시";
            txtMsgId.Text = _flLogEntry.TagName;
            txtMatchedTab.Text = _flLogEntry.ShortModuleName;

            // F/L 로그는 보라색
            txtDirection.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(156, 39, 176));

            // 필드 목록
            var fields = new Dictionary<string, string>
            {
                ["태그명"] = _flLogEntry.TagName,
                ["타입"] = _flLogEntry.DataType,
                ["모듈"] = _flLogEntry.ModuleName,
                ["파일"] = _flLogEntry.SourceFile
            };

            // Structure 필드 추가
            if (_flLogEntry.IsStructure && _flLogEntry.Fields.Count > 0)
            {
                foreach (var field in _flLogEntry.Fields)
                {
                    fields[$"📦 {field.Key}"] = field.Value;
                }
            }
            else
            {
                fields["값"] = _flLogEntry.Value;
            }

            var fieldList = fields
                .Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                .ToList();
            dgFields.ItemsSource = fieldList;

            // Raw Data
            txtRawData.Text = _flLogEntry.RawLine;
        }

        /// <summary>
        /// Raw Data를 보기 좋게 포맷팅
        /// </summary>
        private string FormatRawData(string rawData)
        {
            if (string.IsNullOrEmpty(rawData))
                return "(데이터 없음)";

            // 기본적으로 들여쓰기 추가
            return rawData
                .Replace("><", ">\n<")
                .Replace("{", "{\n  ")
                .Replace("}", "\n}")
                .Replace(",", ",\n  ");
        }

        /// <summary>
        /// XML을 보기 좋게 포맷팅
        /// </summary>
        private string FormatXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                return "(데이터 없음)";

            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                return doc.ToString();
            }
            catch
            {
                // 파싱 실패시 기본 포맷팅
                return xml.Replace("><", ">\n<");
            }
        }

        private void BtnCopyRawData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dataToCopy = _logEntry?.RawData ?? _dataLogEntry?.RawData ?? _exceptionLogEntry?.RawData ?? _flLogEntry?.RawLine ?? "";
                Clipboard.SetText(dataToCopy);
                MessageBox.Show("Raw Data가 클립보드에 복사되었습니다.", "복사 완료", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"복사 실패: {ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
