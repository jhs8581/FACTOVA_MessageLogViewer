using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer
{
    public partial class FLTagSelectWindow : Window
    {
        private List<TagSelectItem> allItems = new();
        public List<string> SelectedTags { get; private set; } = new();

        public FLTagSelectWindow(List<string> availableTags, List<string>? alreadySelectedTags = null)
        {
            InitializeComponent();

            alreadySelectedTags ??= new List<string>();
            allItems = availableTags.Select(t => new TagSelectItem
            {
                TagName = t,
                IsSelected = alreadySelectedTags.Contains(t)
            }).ToList();
            listBoxTags.ItemsSource = allItems;
            UpdateSelectedCount();
        }

        private void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = txtFilter.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
            {
                listBoxTags.ItemsSource = allItems;
            }
            else
            {
                listBoxTags.ItemsSource = allItems.Where(t => t.TagName.ToLower().Contains(filter)).ToList();
            }
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleItems = listBoxTags.ItemsSource as IEnumerable<TagSelectItem>;
            if (visibleItems != null)
            {
                foreach (var item in visibleItems)
                {
                    item.IsSelected = true;
                }
            }
            listBoxTags.Items.Refresh();
            UpdateSelectedCount();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in allItems)
            {
                item.IsSelected = false;
            }
            listBoxTags.Items.Refresh();
            UpdateSelectedCount();
        }

        private void UpdateSelectedCount()
        {
            var count = allItems.Count(t => t.IsSelected);
            txtSelectedCount.Text = $"선택: {count}개";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectedTags = allItems.Where(t => t.IsSelected).Select(t => t.TagName).ToList();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class TagSelectItem : INotifyPropertyChanged
    {
        private bool isSelected;

        public string TagName { get; set; } = "";

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
