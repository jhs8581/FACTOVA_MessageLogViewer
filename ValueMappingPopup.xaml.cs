using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class ValueMappingPopup : Window
    {
        private ObservableCollection<ValueMappingItem> mappingItems = new();

        /// <summary>
        /// 결과 문자열 (예: "1:자동,2:수동")
        /// </summary>
        public string ResultMapping { get; private set; } = "";

        public ValueMappingPopup(string currentMapping)
        {
            InitializeComponent();

            // 기존 매핑 파싱
            ParseMapping(currentMapping);

            dgMappings.ItemsSource = mappingItems;

            // 컬렉션 변경 시 미리보기 업데이트
            mappingItems.CollectionChanged += (s, e) => UpdatePreview();
            UpdatePreview();
        }

        /// <summary>
        /// 기존 매핑 문자열 파싱 (예: "1:자동,2:수동")
        /// </summary>
        private void ParseMapping(string mapping)
        {
            if (string.IsNullOrWhiteSpace(mapping))
                return;

            var pairs = mapping.Split(',');
            foreach (var pair in pairs)
            {
                var parts = pair.Split(':');
                if (parts.Length == 2)
                {
                    var item = new ValueMappingItem
                    {
                        Key = parts[0].Trim(),
                        DisplayName = parts[1].Trim()
                    };
                    item.PropertyChanged += (s, e) => UpdatePreview();
                    mappingItems.Add(item);
                }
            }
        }

        /// <summary>
        /// 미리보기 업데이트
        /// </summary>
        private void UpdatePreview()
        {
            var validItems = mappingItems
                .Where(m => !string.IsNullOrWhiteSpace(m.Key) && !string.IsNullOrWhiteSpace(m.DisplayName))
                .Select(m => $"{m.Key}:{m.DisplayName}");

            txtPreview.Text = string.Join(",", validItems);
        }

        private void BtnAddRow_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new ValueMappingItem();
            newItem.PropertyChanged += (s, ev) => UpdatePreview();
            mappingItems.Add(newItem);
        }

        private void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is ValueMappingItem item)
            {
                mappingItems.Remove(item);
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 유효한 매핑만 결과로 저장
            var validItems = mappingItems
                .Where(m => !string.IsNullOrWhiteSpace(m.Key) && !string.IsNullOrWhiteSpace(m.DisplayName))
                .Select(m => $"{m.Key.Trim()}:{m.DisplayName.Trim()}");

            ResultMapping = string.Join(",", validItems);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// 값 매핑 아이템
    /// </summary>
    public class ValueMappingItem : INotifyPropertyChanged
    {
        private string _key = "";
        private string _displayName = "";

        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    OnPropertyChanged(nameof(Key));
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
