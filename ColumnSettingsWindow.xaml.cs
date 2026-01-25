using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class ColumnSettingsWindow : Window
    {
        private string logFilePath;
        private ObservableCollection<FieldSettingItem> fieldItems = new();

        public bool SettingsApplied { get; private set; } = false;

        public ColumnSettingsWindow(string logFilePath)
        {
            InitializeComponent();
            this.logFilePath = logFilePath;
            
            dgFields.ItemsSource = fieldItems;
            
            LoadPresetList();
            AnalyzeAndLoadFields();
        }

        private void LoadPresetList()
        {
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");
            
            foreach (var preset in ColumnSettingsManager.GetPresetNames())
            {
                cboPresets.Items.Add(preset);
            }

            cboPresets.SelectedIndex = 0;
        }

        private void AnalyzeAndLoadFields()
        {
            fieldItems.Clear();

            var analysisResults = LogFieldAnalyzer.AnalyzeFields(logFilePath);
            var currentSettings = ColumnSettingsManager.CurrentSettings;

            int order = 1;
            
            // Add existing settings in order
            foreach (var config in currentSettings.Fields.OrderBy(f => f.Order))
            {
                var analysisResult = analysisResults.FirstOrDefault(r => r.FieldName == config.FieldName);
                
                fieldItems.Add(new FieldSettingItem
                {
                    Order = order++,
                    FieldName = config.FieldName,
                    DisplayName = config.DisplayName,
                    DisplayType = config.DisplayType,
                    ColumnWidth = config.ColumnWidth,
                    SampleValues = analysisResult?.SampleValues ?? new()
                });
            }

            // Add new fields from analysis
            foreach (var result in analysisResults)
            {
                if (!fieldItems.Any(f => f.FieldName == result.FieldName))
                {
                    fieldItems.Add(new FieldSettingItem
                    {
                        Order = order++,
                        FieldName = result.FieldName,
                        DisplayName = result.FieldName,
                        DisplayType = FieldDisplayType.Summary,
                        ColumnWidth = 100,
                        SampleValues = result.SampleValues
                    });
                }
            }

            UpdateOrders();
        }

        private void UpdateOrders()
        {
            int order = 1;
            foreach (var item in fieldItems)
            {
                item.Order = order++;
            }
            dgFields.Items.Refresh();
        }

        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var selected = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selected))
                return;

            ColumnSettings? settings;
            if (selected == "Default")
            {
                settings = ColumnSettingsManager.CreateDefaultSettings();
            }
            else
            {
                settings = ColumnSettingsManager.LoadPreset(selected);
            }

            if (settings != null)
            {
                ApplySettingsToGrid(settings);
            }
        }

        private void ApplySettingsToGrid(ColumnSettings settings)
        {
            foreach (var item in fieldItems)
            {
                var config = settings.Fields.FirstOrDefault(f => f.FieldName == item.FieldName);
                if (config != null)
                {
                    item.DisplayName = config.DisplayName;
                    item.DisplayType = config.DisplayType;
                    item.ColumnWidth = config.ColumnWidth;
                }
            }
            dgFields.Items.Refresh();
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            var settings = CreateSettingsFromGrid();
            ColumnSettingsManager.SaveCurrentSettings(settings);
            MessageBox.Show("Settings saved.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveAsPreset_Click(object sender, RoutedEventArgs e)
        {
            var name = txtPresetName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter preset name.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = CreateSettingsFromGrid();
            ColumnSettingsManager.SaveSettingsAsPreset(settings, name);
            
            LoadPresetList();
            MessageBox.Show($"Saved as '{name}'.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private ColumnSettings CreateSettingsFromGrid()
        {
            var settings = new ColumnSettings
            {
                Name = "User Settings",
                Fields = fieldItems.Select((item, index) => new FieldConfig
                {
                    FieldName = item.FieldName,
                    DisplayName = item.DisplayName,
                    DisplayType = item.DisplayType,
                    ColumnWidth = item.ColumnWidth,
                    Order = index
                }).ToList()
            };
            return settings;
        }

        private void BtnAllSummary_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in fieldItems)
            {
                item.DisplayType = FieldDisplayType.Summary;
            }
            UpdateOrders();
        }

        private void BtnAllHidden_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in fieldItems)
            {
                item.DisplayType = FieldDisplayType.Hidden;
            }
            UpdateOrders();
        }

        private void BtnAddField_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new FieldSettingItem
            {
                Order = fieldItems.Count + 1,
                FieldName = "NEW_FIELD",
                DisplayName = "New Field",
                DisplayType = FieldDisplayType.Column,
                ColumnWidth = 100
            };
            fieldItems.Add(newItem);
            UpdateOrders();
            dgFields.SelectedItem = newItem;
            dgFields.ScrollIntoView(newItem);
        }

        private void BtnRemoveField_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgFields.SelectedItem as FieldSettingItem;
            if (selected != null)
            {
                fieldItems.Remove(selected);
                UpdateOrders();
            }
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgFields.SelectedItem as FieldSettingItem;
            if (selected == null) return;

            int index = fieldItems.IndexOf(selected);
            if (index > 0)
            {
                fieldItems.Move(index, index - 1);
                UpdateOrders();
                dgFields.SelectedItem = selected;
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgFields.SelectedItem as FieldSettingItem;
            if (selected == null) return;

            int index = fieldItems.IndexOf(selected);
            if (index < fieldItems.Count - 1)
            {
                fieldItems.Move(index, index + 1);
                UpdateOrders();
                dgFields.SelectedItem = selected;
            }
        }

        private void BtnReanalyze_Click(object sender, RoutedEventArgs e)
        {
            AnalyzeAndLoadFields();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Reset to default?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var defaultSettings = ColumnSettingsManager.CreateDefaultSettings();
                ApplySettingsToGrid(defaultSettings);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var settings = CreateSettingsFromGrid();
            ColumnSettingsManager.CurrentSettings = settings;
            
            SettingsApplied = true;
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Grid binding item
    /// </summary>
    public class FieldSettingItem : INotifyPropertyChanged
    {
        private int _order;
        private string _fieldName = "";
        private string _displayName = "";
        private string _displayTypeString = "Summary";
        private int _columnWidth = 100;

        public int Order
        {
            get => _order;
            set { _order = value; OnPropertyChanged(nameof(Order)); }
        }

        public string FieldName
        {
            get => _fieldName;
            set { _fieldName = value; OnPropertyChanged(nameof(FieldName)); }
        }

        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        // String for ComboBox binding
        public string DisplayTypeString
        {
            get => _displayTypeString;
            set 
            { 
                _displayTypeString = value; 
                OnPropertyChanged(nameof(DisplayTypeString)); 
                OnPropertyChanged(nameof(DisplayType));
            }
        }

        // Enum for internal use
        public FieldDisplayType DisplayType
        {
            get => Enum.TryParse<FieldDisplayType>(_displayTypeString, out var result) ? result : FieldDisplayType.Summary;
            set 
            { 
                _displayTypeString = value.ToString();
                OnPropertyChanged(nameof(DisplayTypeString));
                OnPropertyChanged(nameof(DisplayType)); 
            }
        }

        public int ColumnWidth
        {
            get => _columnWidth;
            set { _columnWidth = value; OnPropertyChanged(nameof(ColumnWidth)); }
        }

        public System.Collections.Generic.List<string> SampleValues { get; set; } = new();
        
        public string SamplePreview => SampleValues.Count > 0 
            ? string.Join(", ", SampleValues.Take(3)) 
            : "(no value)";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
