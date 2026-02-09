using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer.Popup
{
    public partial class TabSelectionPopup : Window
    {
        private ObservableCollection<TabSelectionItem> tabItems = new();
        
        public List<string> SelectedTabs { get; private set; } = new();

        public TabSelectionPopup(List<string> availableTabs, List<string> currentSelection)
        {
            InitializeComponent();

            System.Diagnostics.Debug.WriteLine($"🔍 TabSelectionPopup 생성:");
            System.Diagnostics.Debug.WriteLine($"  - 전체 탭: {availableTabs.Count}개");
            System.Diagnostics.Debug.WriteLine($"  - 선택된 탭: {currentSelection.Count}개");

            // 탭 아이템 생성
            foreach (var tabName in availableTabs)
            {
                bool isSelected = currentSelection.Contains(tabName);
                System.Diagnostics.Debug.WriteLine($"  - {tabName}: {(isSelected ? "✓" : "✗")}");
                
                tabItems.Add(new TabSelectionItem
                {
                    TabName = tabName,
                    IsSelected = isSelected
                });
            }

            icTabs.ItemsSource = tabItems;
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tabItems)
            {
                item.IsSelected = true;
            }
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tabItems)
            {
                item.IsSelected = false;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 선택된 탭 목록 수집
            SelectedTabs = tabItems.Where(t => t.IsSelected).Select(t => t.TabName).ToList();
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
    /// 탭 선택 아이템 (체크박스 바인딩용)
    /// </summary>
    public class TabSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public string TabName { get; set; } = "";
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
