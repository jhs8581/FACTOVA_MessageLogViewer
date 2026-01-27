using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer
{
    public partial class ColumnSettingsWindow : Window
    {
        private string logFilePath;
        private ObservableCollection<FieldSettingItem> fieldItems = new();
        
        // 탭 설정 관련
        private ObservableCollection<TabConfig> tabs = new();
        private TabConfig? selectedTab;
        private bool isUpdating = false;
        private string? initialPresetName;

        public bool SettingsApplied { get; private set; } = false;

        /// <summary>
        /// 발견된 필드 목록 (콤보박스 바인딩용)
        /// </summary>
        public List<string> FieldList => LogFieldAnalyzer.DiscoveredFields;

        public ColumnSettingsWindow(string logFilePath, string? selectedPresetName = null)
        {
            InitializeComponent();
            this.logFilePath = logFilePath;
            this.initialPresetName = selectedPresetName;
            
            dgFields.ItemsSource = fieldItems;
            
            LoadPresetList();
            AnalyzeAndLoadFields();
            LoadTabSettings();
        }

        #region 프리셋 관리

        private void LoadPresetList()
        {
            isLoadingPreset = true;
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");
            
            foreach (var preset in ColumnSettingsManager.GetPresetNames())
            {
                cboPresets.Items.Add(preset);
            }

            // 전달받은 프리셋 이름으로 선택, 없으면 현재 설정 이름으로 선택
            var targetPreset = initialPresetName ?? ColumnSettingsManager.CurrentSettings.Name;
            int matchIndex = 0;
            for (int i = 0; i < cboPresets.Items.Count; i++)
            {
                if (cboPresets.Items[i]?.ToString() == targetPreset)
                {
                    matchIndex = i;
                    break;
                }
            }
            
            cboPresets.SelectedIndex = matchIndex;
            isLoadingPreset = false;
        }

        #endregion

        #region 컬럼 설정

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

            UpdateFieldOrders();
        }

        private void UpdateFieldOrders()
        {
            int order = 1;
            foreach (var item in fieldItems)
            {
                item.Order = order++;
            }
            dgFields.Items.Refresh();
        }

        private bool isLoadingPreset = false;

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isLoadingPreset) return;

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
                ApplyTabSettingsFromSettings(settings);
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
            var selected = cboPresets.SelectedItem?.ToString();
            var settings = CreateSettingsFromAll();
            
            if (string.IsNullOrEmpty(selected) || selected == "Default")
            {
                // Default 선택 시 현재 설정에 저장
                ColumnSettingsManager.SaveCurrentSettings(settings);
                MessageBox.Show("현재 설정에 저장되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // 선택한 프리셋에 저장
                ColumnSettingsManager.SaveSettingsAsPreset(settings, selected);
                MessageBox.Show($"'{selected}' 프리셋에 저장되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSaveAsPreset_Click(object sender, RoutedEventArgs e)
        {
            var name = txtPresetName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("프리셋 이름을 입력하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = CreateSettingsFromAll();
            ColumnSettingsManager.SaveSettingsAsPreset(settings, name);
            
            LoadPresetList();
            MessageBox.Show($"'{name}'으로 저장되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private ColumnSettings CreateSettingsFromAll()
        {
            // 탭 순서 업데이트
            UpdateTabOrders();

            // 현재 선택된 프리셋 이름 사용
            var presetName = cboPresets.SelectedItem?.ToString() ?? "Default";

            var settings = new ColumnSettings
            {
                Name = presetName,
                Fields = fieldItems.Select((item, index) => new FieldConfig
                {
                    FieldName = item.FieldName,
                    DisplayName = item.DisplayName,
                    DisplayType = item.DisplayType,
                    ColumnWidth = item.ColumnWidth,
                    Order = index
                }).ToList(),
                TabSettings = new TabSettings
                {
                    Tabs = tabs.ToList(),
                    LastSelectedTabIndex = 0
                },
                FontSize = ColumnSettingsManager.CurrentSettings.FontSize
            };
            return settings;
        }

        private void BtnAllSummary_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in fieldItems)
            {
                item.DisplayType = FieldDisplayType.Summary;
            }
            UpdateFieldOrders();
        }

        private void BtnAllHidden_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in fieldItems)
            {
                item.DisplayType = FieldDisplayType.Hidden;
            }
            UpdateFieldOrders();
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgFields.SelectedItem as FieldSettingItem;
            if (selected == null) return;

            int index = fieldItems.IndexOf(selected);
            if (index > 0)
            {
                fieldItems.Move(index, index - 1);
                UpdateFieldOrders();
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
                UpdateFieldOrders();
                dgFields.SelectedItem = selected;
            }
        }

        private void BtnReanalyze_Click(object sender, RoutedEventArgs e)
        {
            AnalyzeAndLoadFields();
        }

        #endregion

        #region 탭 설정

        private void LoadTabSettings()
        {
            var settings = ColumnSettingsManager.CurrentSettings;
            ApplyTabSettingsFromSettings(settings);
        }

        private void ApplyTabSettingsFromSettings(ColumnSettings settings)
        {
            System.Diagnostics.Debug.WriteLine($"📋 ApplyTabSettingsFromSettings 호출");
            System.Diagnostics.Debug.WriteLine($"   - TabSettings null?: {settings.TabSettings == null}");
            System.Diagnostics.Debug.WriteLine($"   - Tabs null?: {settings.TabSettings?.Tabs == null}");
            System.Diagnostics.Debug.WriteLine($"   - Tabs count: {settings.TabSettings?.Tabs?.Count ?? 0}");
            
            tabs.Clear();
            if (settings.TabSettings?.Tabs != null)
            {
                foreach (var tab in settings.TabSettings.Tabs)
                {
                    System.Diagnostics.Debug.WriteLine($"   - Tab: {tab.Name}, IsEnabled: {tab.IsEnabled}, IsIntegrated: {tab.IsIntegrated}");
                    
                    var copy = new TabConfig
                    {
                        Name = tab.Name,
                        Order = tab.Order,
                        IsEnabled = tab.IsEnabled,
                        IsIntegrated = tab.IsIntegrated,
                        Conditions = tab.Conditions?.Select(c => new TabFilterCondition
                        {
                            FieldName = c.FieldName,
                            Value = c.Value,
                            ExactMatch = c.ExactMatch
                        }).ToList() ?? new List<TabFilterCondition>(),
                        ConditionGroups = tab.ConditionGroups?.Select(g => new ConditionGroup
                        {
                            Name = g.Name,
                            Conditions = g.Conditions?.Select(c => new TabFilterCondition
                            {
                                FieldName = c.FieldName,
                                Value = c.Value,
                                ExactMatch = c.ExactMatch
                            }).ToList() ?? new List<TabFilterCondition>()
                        }).ToList() ?? new List<ConditionGroup>()
                    };

                    // 이전 호환: Conditions가 있고 ConditionGroups가 없으면 변환
                    if (copy.ConditionGroups.Count == 0 && copy.Conditions.Count > 0)
                    {
                        copy.ConditionGroups.Add(new ConditionGroup
                        {
                            Name = "Group 1",
                            Conditions = copy.Conditions.ToList()
                        });
                        copy.Conditions.Clear();
                    }

                    tabs.Add(copy);
                }
            }



            System.Diagnostics.Debug.WriteLine($"   - 최종 tabs count: {tabs.Count}");

            // 탭이 없으면 기본 통합 탭 추가
            if (tabs.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ 탭이 없어서 기본 탭 추가");
                tabs.Add(new TabConfig
                {
                    Name = "전체 로그",
                    Order = 0,
                    IsIntegrated = true,
                    IsEnabled = true
                });
            }

            listBoxTabs.ItemsSource = null;
            listBoxTabs.ItemsSource = tabs;
            
            if (tabs.Count > 0)
            {
                listBoxTabs.SelectedIndex = 0;
            }
        }

        private void ListBoxTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedTab = listBoxTabs.SelectedItem as TabConfig;
            UpdateTabDetailPanel();
        }

        private void UpdateTabDetailPanel()
        {
            if (selectedTab == null)
            {
                panelTabDetails.IsEnabled = false;
                return;
            }

            isUpdating = true;
            panelTabDetails.IsEnabled = true;

            txtTabName.Text = selectedTab.Name;
            chkIsIntegrated.IsChecked = selectedTab.IsIntegrated;
            
            itemsConditionGroups.ItemsSource = null;
            itemsConditionGroups.ItemsSource = selectedTab.ConditionGroups;
            
            itemsConditionGroups.IsEnabled = !selectedTab.IsIntegrated;

            isUpdating = false;
        }

        private void TxtTabName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdating || selectedTab == null) return;
            
            selectedTab.Name = txtTabName.Text;
            RefreshTabList();
        }

        private void ChkIsIntegrated_Changed(object sender, RoutedEventArgs e)
        {
            if (isUpdating || selectedTab == null) return;
            
            selectedTab.IsIntegrated = chkIsIntegrated.IsChecked == true;
            itemsConditionGroups.IsEnabled = !selectedTab.IsIntegrated;
            RefreshTabList();
        }

        private void TabEnabled_Changed(object sender, RoutedEventArgs e)
        {
            // 체크박스 변경 시 자동 반영됨
        }

        private void RefreshTabList()
        {
            var selectedIndex = listBoxTabs.SelectedIndex;
            listBoxTabs.ItemsSource = null;
            listBoxTabs.ItemsSource = tabs;
            listBoxTabs.SelectedIndex = selectedIndex;
        }

        private void BtnAddTab_Click(object sender, RoutedEventArgs e)
        {
            var newTab = new TabConfig
            {
                Name = $"새 탭 {tabs.Count + 1}",
                Order = tabs.Count,
                IsEnabled = true,
                IsIntegrated = false,
                ConditionGroups = new List<ConditionGroup>()
            };

            tabs.Add(newTab);
            RefreshTabList();
            listBoxTabs.SelectedItem = newTab;
        }

        private void BtnRemoveTab_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (tabs.Count <= 1)
            {
                MessageBox.Show("최소 1개의 탭이 필요합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"'{selectedTab.Name}' 탭을 삭제하시겠습니까?",
                "확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                var index = tabs.IndexOf(selectedTab);
                tabs.Remove(selectedTab);
                
                RefreshTabList();
                
                if (tabs.Count > 0)
                {
                    listBoxTabs.SelectedIndex = Math.Min(index, tabs.Count - 1);
                }
            }
        }

        private void BtnTabMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var index = tabs.IndexOf(selectedTab);
            if (index > 0)
            {
                tabs.Move(index, index - 1);
                UpdateTabOrders();
                RefreshTabList();
            }
        }

        private void BtnTabMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var index = tabs.IndexOf(selectedTab);
            if (index < tabs.Count - 1)
            {
                tabs.Move(index, index + 1);
                UpdateTabOrders();
                RefreshTabList();
            }
        }

        private void UpdateTabOrders()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].Order = i;
            }
        }

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            selectedTab.ConditionGroups ??= new List<ConditionGroup>();
            selectedTab.ConditionGroups.Add(new ConditionGroup
            {
                Name = $"그룹 {selectedTab.ConditionGroups.Count + 1}",
                Conditions = new List<TabFilterCondition>
                {
                    new TabFilterCondition { FieldName = "MSGID", Value = "", ExactMatch = true }
                }
            });

            UpdateTabDetailPanel();
        }

        private void BtnRemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (sender is Button button && button.Tag is ConditionGroup group)
            {
                selectedTab.ConditionGroups?.Remove(group);
                UpdateTabDetailPanel();
            }
        }

        private void BtnAddConditionToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ConditionGroup group)
            {
                group.Conditions ??= new List<TabFilterCondition>();
                group.Conditions.Add(new TabFilterCondition
                {
                    FieldName = "MSGID",
                    Value = "",
                    ExactMatch = true
                });

                UpdateTabDetailPanel();
            }
        }

        private void BtnRemoveConditionInGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (sender is Button button && button.Tag is TabFilterCondition condition)
            {
                foreach (var group in selectedTab.ConditionGroups ?? Enumerable.Empty<ConditionGroup>())
                {
                    if (group.Conditions?.Remove(condition) == true)
                    {
                        break;
                    }
                }
                UpdateTabDetailPanel();
            }
        }

        #endregion

        #region 공통 버튼

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("기본값으로 초기화하시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var defaultSettings = ColumnSettingsManager.CreateDefaultSettings();
                ApplySettingsToGrid(defaultSettings);
                ApplyTabSettingsFromSettings(defaultSettings);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var settings = CreateSettingsFromAll();
            ColumnSettingsManager.CurrentSettings = settings;
            
            SettingsApplied = true;
            DialogResult = true;
            Close();
        }

        #endregion
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
