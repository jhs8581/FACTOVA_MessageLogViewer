using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;


namespace FACTOVA_MessageLogViewer
{
    public partial class FLColumnSettingsWindow : Window
    {
        private ObservableCollection<FLTagConfig> tagConfigs = new();
        private ObservableCollection<FLFieldConfig> fieldConfigs = new();
        private ObservableCollection<FLTabConfig> tabConfigs = new();
        private FLTabConfig? selectedTab;
        private UnifiedPreset currentPreset = UnifiedPreset.CreateDefault();

        // 현재 로드된 F/L 로그에서 태그 추출을 위한 콜백
        public Func<IEnumerable<FLLogEntry>>? GetCurrentLogEntries { get; set; }

        public FLColumnSettingsWindow(string presetName = "Default")
        {
            InitializeComponent();
            LoadPresetList();
            SelectPreset(presetName);
        }

        #region 프리셋 관리

        private void LoadPresetList()
        {
            // 통합 프리셋 목록 사용
            var presets = UnifiedPresetManager.GetPresetNames();
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");
            foreach (var name in presets)
            {
                if (!cboPresets.Items.Contains(name))
                    cboPresets.Items.Add(name);
            }
        }

        private void SelectPreset(string name)
        {
            for (int i = 0; i < cboPresets.Items.Count; i++)
            {
                if (cboPresets.Items[i].ToString() == name)
                {
                    cboPresets.SelectedIndex = i;
                    return;
                }
            }

            if (cboPresets.Items.Count > 0)
                cboPresets.SelectedIndex = 0;
        }

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboPresets.SelectedItem == null) return;

            var presetName = cboPresets.SelectedItem.ToString() ?? "Default";
            var preset = UnifiedPresetManager.LoadPreset(presetName) ?? UnifiedPreset.CreateDefault();
            
            currentPreset = preset;
            LoadPresetToUI(preset);
        }

        private void LoadPresetToUI(UnifiedPreset preset)
        {
            var flSettings = preset.FLSettings ?? FLPresetSettings.CreateDefault();

            // 태그 설정 로드
            tagConfigs.Clear();
            foreach (var config in flSettings.TagConfigs)
            {
                tagConfigs.Add(config);
            }
            dgTags.ItemsSource = tagConfigs;

            // 필드 설정 로드
            fieldConfigs.Clear();
            foreach (var config in flSettings.FieldConfigs)
            {
                fieldConfigs.Add(config);
            }
            dgFields.ItemsSource = fieldConfigs;

            // 탭 설정 로드
            tabConfigs.Clear();
            foreach (var tab in flSettings.TabSettings?.Tabs ?? new List<FLTabConfig>())
            {
                tabConfigs.Add(tab);
            }
            listBoxTabs.ItemsSource = tabConfigs;

            if (tabConfigs.Count > 0)
                listBoxTabs.SelectedIndex = 0;
        }

        private void SaveUIToPreset()
        {
            currentPreset.FLSettings = new FLPresetSettings
            {
                TagConfigs = tagConfigs.ToList(),
                FieldConfigs = fieldConfigs.ToList(),
                TabSettings = new FLTabSettings
                {
                    Tabs = tabConfigs.ToList(),
                    LastSelectedTabIndex = listBoxTabs.SelectedIndex >= 0 ? listBoxTabs.SelectedIndex : 0
                }
            };
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToPreset();
            UnifiedPresetManager.SavePreset(currentPreset);
            MessageBox.Show($"프리셋 '{currentPreset.Name}'이(가) 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveAsPreset_Click(object sender, RoutedEventArgs e)
        {
            var newName = txtPresetName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                MessageBox.Show("프리셋 이름을 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveUIToPreset();
            currentPreset.Name = newName;
            UnifiedPresetManager.SavePreset(currentPreset);

            LoadPresetList();
            SelectPreset(newName);

            MessageBox.Show($"프리셋 '{newName}'이(가) 저장되었습니다.", "저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString();
            if (presetName == "Default")
            {
                MessageBox.Show("Default 프리셋은 삭제할 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"프리셋 '{presetName}'을(를) 삭제하시겠습니까?", "삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                UnifiedPresetManager.DeletePreset(presetName!);
                LoadPresetList();
                SelectPreset("Default");
            }
        }

        private void BtnOpenPresetFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = UnifiedPresetManager.GetPresetFolderPath();
            Directory.CreateDirectory(folderPath);
            Process.Start("explorer.exe", folderPath);
        }

        #endregion

        #region 태그 설정

        private void BtnExtractTags_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentLogEntries == null)
            {
                MessageBox.Show("현재 로드된 F/L 로그가 없습니다.\n먼저 F/L 뷰어에서 로그를 로드해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entries = GetCurrentLogEntries();
            var tagNames = entries.Select(e => e.TagName).Distinct().OrderBy(t => t).ToList();

            if (tagNames.Count == 0)
            {
                MessageBox.Show("추출할 태그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 기존 설정 유지하면서 새 태그 추가
            var existingTags = tagConfigs.ToDictionary(t => t.TagName, t => t);
            int order = tagConfigs.Count;

            foreach (var tagName in tagNames)
            {
                if (!existingTags.ContainsKey(tagName))
                {
                    var sampleEntry = entries.FirstOrDefault(e => e.TagName == tagName);
                    tagConfigs.Add(new FLTagConfig
                    {
                        Order = ++order,
                        TagName = tagName,
                        DisplayName = "",
                        IsEnabled = true,
                        SampleValue = sampleEntry?.DisplayValue ?? ""
                    });
                }
                else
                {
                    // 샘플 값 업데이트
                    var sampleEntry = entries.FirstOrDefault(e => e.TagName == tagName);
                    existingTags[tagName].SampleValue = sampleEntry?.DisplayValue ?? "";
                }
            }

            dgTags.Items.Refresh();
            MessageBox.Show($"{tagNames.Count}개 태그 중 {tagNames.Count - existingTags.Count}개가 새로 추가되었습니다.", "태그 추출 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgTags.SelectedItems.Cast<FLTagConfig>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected.OrderBy(t => tagConfigs.IndexOf(t)))
            {
                int index = tagConfigs.IndexOf(item);
                if (index > 0)
                {
                    tagConfigs.Move(index, index - 1);
                }
            }

            UpdateTagOrder();
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selected = dgTags.SelectedItems.Cast<FLTagConfig>().ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected.OrderByDescending(t => tagConfigs.IndexOf(t)))
            {
                int index = tagConfigs.IndexOf(item);
                if (index < tagConfigs.Count - 1)
                {
                    tagConfigs.Move(index, index + 1);
                }
            }

            UpdateTagOrder();
        }

        private void UpdateTagOrder()
        {
            for (int i = 0; i < tagConfigs.Count; i++)
            {
                tagConfigs[i].Order = i + 1;
            }
        }

        private void ChkShowOnlyEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (chkShowOnlyEnabled.IsChecked == true)
            {
                dgTags.ItemsSource = tagConfigs.Where(t => t.IsEnabled).ToList();
            }
            else
            {
                dgTags.ItemsSource = tagConfigs;
            }
        }

        private void BtnBulkEnable_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tagConfigs.Where(t => t.IsSelected))
            {
                item.IsEnabled = true;
            }
            dgTags.Items.Refresh();
        }

        private void BtnBulkDisable_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in tagConfigs.Where(t => t.IsSelected))
            {
                item.IsEnabled = false;
            }
            dgTags.Items.Refresh();
        }

        private void BtnBulkSetDisplayName_Click(object sender, RoutedEventArgs e)
        {
            var displayName = txtBulkDisplayName.Text.Trim();
            foreach (var item in tagConfigs.Where(t => t.IsSelected))
            {
                item.DisplayName = displayName;
            }
            dgTags.Items.Refresh();
        }

        #endregion

        #region 탭 설정

        private void ListBoxTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedTab = listBoxTabs.SelectedItem as FLTabConfig;
            UpdateTabDetails();
        }

        private void UpdateTabDetails()
        {
            if (selectedTab == null)
            {
                panelTabDetails.IsEnabled = false;
                return;
            }

            panelTabDetails.IsEnabled = true;
            txtTabName.Text = selectedTab.Name;
            chkIsIntegrated.IsChecked = selectedTab.IsIntegrated;
            itemsConditionGroups.ItemsSource = selectedTab.ConditionGroups;
        }

        private void TxtTabName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (selectedTab != null)
            {
                selectedTab.Name = txtTabName.Text;
                listBoxTabs.Items.Refresh();
            }
        }

        private void ChkIsIntegrated_Changed(object sender, RoutedEventArgs e)
        {
            if (selectedTab != null)
            {
                selectedTab.IsIntegrated = chkIsIntegrated.IsChecked == true;
                listBoxTabs.Items.Refresh();
            }
        }

        private void TabEnabled_Changed(object sender, RoutedEventArgs e)
        {
            listBoxTabs.Items.Refresh();
        }

        private void BtnAddTab_Click(object sender, RoutedEventArgs e)
        {
            var newTab = new FLTabConfig
            {
                Name = $"새 탭 {tabConfigs.Count + 1}",
                IsEnabled = true,
                IsIntegrated = false,
                ConditionGroups = new List<FLConditionGroup>()
            };
            tabConfigs.Add(newTab);
            listBoxTabs.SelectedItem = newTab;
        }

        private void BtnRemoveTab_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (tabConfigs.Count == 1)
            {
                MessageBox.Show("최소 1개의 탭이 필요합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            tabConfigs.Remove(selectedTab);
            if (tabConfigs.Count > 0)
                listBoxTabs.SelectedIndex = 0;
        }

        private void BtnTabMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            int index = tabConfigs.IndexOf(selectedTab);
            if (index > 0)
            {
                tabConfigs.Move(index, index - 1);
                listBoxTabs.SelectedIndex = index - 1;
            }
        }

        private void BtnTabMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            int index = tabConfigs.IndexOf(selectedTab);
            if (index < tabConfigs.Count - 1)
            {
                tabConfigs.Move(index, index + 1);
                listBoxTabs.SelectedIndex = index + 1;
            }
        }

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            selectedTab.ConditionGroups.Add(new FLConditionGroup
            {
                Name = $"그룹 {selectedTab.ConditionGroups.Count + 1}",
                TagNames = new List<string>()
            });

            itemsConditionGroups.ItemsSource = null;
            itemsConditionGroups.ItemsSource = selectedTab.ConditionGroups;
            listBoxTabs.Items.Refresh();
        }

        private void BtnRemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var group = button?.Tag as FLConditionGroup;
            if (group != null)
            {
                selectedTab.ConditionGroups.Remove(group);
                itemsConditionGroups.ItemsSource = null;
                itemsConditionGroups.ItemsSource = selectedTab.ConditionGroups;
                listBoxTabs.Items.Refresh();
            }
        }

        private void BtnAddTagToGroup_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var group = button?.Tag as FLConditionGroup;
            if (group == null) return;

            // 태그 선택 다이얼로그
            var selectWindow = new FLTagSelectWindow(tagConfigs.Where(t => t.IsEnabled).Select(t => t.TagName).ToList());
            selectWindow.Owner = this;

            if (selectWindow.ShowDialog() == true && selectWindow.SelectedTags.Count > 0)
            {
                foreach (var tag in selectWindow.SelectedTags)
                {
                    if (!group.TagNames.Contains(tag))
                    {
                        group.TagNames.Add(tag);
                    }
                }

                itemsConditionGroups.ItemsSource = null;
                itemsConditionGroups.ItemsSource = selectedTab?.ConditionGroups;
                listBoxTabs.Items.Refresh();
            }
        }

        #endregion

        #region 필드 설정

        private void BtnExtractFields_Click(object sender, RoutedEventArgs e)
        {
            var entries = GetCurrentLogEntries?.Invoke()?.ToList();
            if (entries == null || entries.Count == 0)
            {
                MessageBox.Show("현재 로드된 F/L 로그가 없습니다.\n먼저 F/L 뷰어에서 로그를 로드해주세요.", 
                    "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Structure 타입 로그에서 필드 추출
            var structureEntries = entries.Where(e => e.IsStructure && e.Fields.Count > 0).ToList();
            if (structureEntries.Count == 0)
            {
                MessageBox.Show("Structure 타입의 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var allFields = new Dictionary<string, string>(); // 필드명 → 샘플값

            foreach (var entry in structureEntries)
            {
                foreach (var field in entry.Fields)
                {
                    if (!allFields.ContainsKey(field.Key))
                    {
                        allFields[field.Key] = field.Value;
                    }
                }
            }

            // 기존 필드와 병합
            int order = fieldConfigs.Count;
            int addedCount = 0;

            foreach (var field in allFields)
            {
                if (!fieldConfigs.Any(f => f.FieldName == field.Key))
                {
                    fieldConfigs.Add(new FLFieldConfig
                    {
                        Order = ++order,
                        FieldName = field.Key,
                        DisplayName = field.Key,
                        SampleValue = field.Value,
                        ShowAsColumn = false,
                        ColumnWidth = 80
                    });
                    addedCount++;
                }
                else
                {
                    // 샘플 값 업데이트
                    var existing = fieldConfigs.First(f => f.FieldName == field.Key);
                    if (string.IsNullOrEmpty(existing.SampleValue))
                    {
                        existing.SampleValue = field.Value;
                    }
                }
            }

            dgFields.Items.Refresh();
            MessageBox.Show($"필드 {addedCount}개가 추가되었습니다.\n전체 필드: {fieldConfigs.Count}개", 
                "추출 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnFieldMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (dgFields.SelectedItem is FLFieldConfig selected)
            {
                int index = fieldConfigs.IndexOf(selected);
                if (index > 0)
                {
                    fieldConfigs.Move(index, index - 1);
                    UpdateFieldOrders();
                }
            }
        }

        private void BtnFieldMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (dgFields.SelectedItem is FLFieldConfig selected)
            {
                int index = fieldConfigs.IndexOf(selected);
                if (index < fieldConfigs.Count - 1)
                {
                    fieldConfigs.Move(index, index + 1);
                    UpdateFieldOrders();
                }
            }
        }

        private void UpdateFieldOrders()
        {
            for (int i = 0; i < fieldConfigs.Count; i++)
            {
                fieldConfigs[i].Order = i + 1;
            }
        }

        private void ChkShowOnlyColumnFields_Changed(object sender, RoutedEventArgs e)
        {
            if (chkShowOnlyColumnFields.IsChecked == true)
            {
                dgFields.ItemsSource = fieldConfigs.Where(f => f.ShowAsColumn).ToList();
            }
            else
            {
                dgFields.ItemsSource = fieldConfigs;
            }
        }

        private void BtnBulkShowColumn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var field in fieldConfigs.Where(f => f.IsSelected))
            {
                field.ShowAsColumn = true;
            }
            dgFields.Items.Refresh();
        }

        private void BtnBulkHideColumn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var field in fieldConfigs.Where(f => f.IsSelected))
            {
                field.ShowAsColumn = false;
            }
            dgFields.Items.Refresh();
        }

        private void BtnEditValueMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FLFieldConfig field)
            {
                // 값 매핑 편집 다이얼로그
                var inputWindow = new Window
                {
                    Title = $"값 매핑 - {field.FieldName}",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                var grid = new Grid { Margin = new Thickness(15) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new TextBlock
                {
                    Text = "값 매핑 (형식: 원본값:표시값, 구분자 쉼표)\n예: 1:장입,2:미장입,True:ON,False:OFF",
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };
                Grid.SetRow(label, 0);
                grid.Children.Add(label);

                var textBox = new TextBox
                {
                    Text = field.ValueMapping,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                Grid.SetRow(textBox, 1);
                grid.Children.Add(textBox);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                Grid.SetRow(buttonPanel, 2);

                var okButton = new Button { Content = "확인", Width = 80, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
                var cancelButton = new Button { Content = "취소", Width = 80, Height = 28 };

                okButton.Click += (s, args) =>
                {
                    field.ValueMapping = textBox.Text.Trim();
                    dgFields.Items.Refresh();
                    inputWindow.DialogResult = true;
                    inputWindow.Close();
                };

                cancelButton.Click += (s, args) =>
                {
                    inputWindow.DialogResult = false;
                    inputWindow.Close();
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                grid.Children.Add(buttonPanel);

                inputWindow.Content = grid;
                inputWindow.ShowDialog();
            }
        }

        #endregion

        #region 버튼 이벤트

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToPreset();
            UnifiedPresetManager.SavePreset(currentPreset);
            UnifiedPresetManager.CurrentPreset = currentPreset;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
