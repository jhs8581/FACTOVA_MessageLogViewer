using FACTOVA_MessageLogViewer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class LogDetailPopup : Window
    {
        private LogEntry _logEntry;

        public LogDetailPopup(LogEntry entry)
        {
            InitializeComponent();
            _logEntry = entry;
            LoadLogDetails();
        }

        private void LoadLogDetails()
        {
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

        private void BtnCopyRawData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_logEntry.RawData);
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
