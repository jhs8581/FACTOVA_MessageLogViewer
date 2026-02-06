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
        private bool isInitializing = true; // 초기화 중 플래그

        // 현재 로드된 F/L 로그에서 태그 추출을 위한 콜백
        public Func<IEnumerable<FLLogEntry>>? GetCurrentLogEntries { get; set; }

        public FLColumnSettingsWindow(string presetName = "Default")
        {
            InitializeComponent();
            
            isInitializing = true;
            LoadPresetList();
            SelectPreset(presetName);
            isInitializing = false;
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
            int selectedIndex = -1;
            
            for (int i = 0; i < cboPresets.Items.Count; i++)
            {
                if (cboPresets.Items[i].ToString() == name)
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0 && cboPresets.Items.Count > 0)
                selectedIndex = 0;

            // 프리셋 로드 (이벤트 핸들러가 아직 동작하지 않으므로 명시적으로 로드)
            if (selectedIndex >= 0)
            {
                cboPresets.SelectedIndex = selectedIndex;
                
                var presetName = cboPresets.Items[selectedIndex].ToString() ?? "Default";
                var preset = UnifiedPresetManager.LoadPreset(presetName);
                
                if (preset == null)
                {
                    preset = UnifiedPreset.CreateDefault();
                    preset.Name = presetName; // 프리셋 이름 보존
                }
                
                currentPreset = preset;
                LoadPresetToUI(preset);
                
                System.Diagnostics.Debug.WriteLine($"🎨 FL 프리셋 초기 로드: {presetName} (태그: {tagConfigs.Count}개, 탭: {tabConfigs.Count}개)");
            }
        }

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 초기화 중이거나 선택된 항목이 없으면 무시
            if (isInitializing || cboPresets.SelectedItem == null) return;

            var presetName = cboPresets.SelectedItem.ToString() ?? "Default";
            var preset = UnifiedPresetManager.LoadPreset(presetName);
            
            if (preset == null)
            {
                preset = UnifiedPreset.CreateDefault();
                preset.Name = presetName; // 프리셋 이름 보존
            }

            currentPreset = preset;
            LoadPresetToUI(preset);
            
            System.Diagnostics.Debug.WriteLine($"🎨 FL 프리셋 로드됨: {presetName}");
        }

        /// <summary>
        /// 탭 변경 이벤트 (프리셋 영역 색상 변경 제거 - 보라색 테마 통일)
        /// </summary>
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 프리셋 영역은 항상 보라색 테마 유지
        }

        private void LoadPresetToUI(UnifiedPreset preset)
        {
            var flSettings = preset.FLSettings ?? FLPresetSettings.CreateDefault();
            
            System.Diagnostics.Debug.WriteLine($"📂 FL LoadPresetToUI: 태그={flSettings.TagConfigs?.Count ?? 0}개, 필드={flSettings.FieldConfigs?.Count ?? 0}개, 탭={flSettings.TabSettings?.Tabs?.Count ?? 0}개");

            // 태그 설정 로드
            tagConfigs.Clear();
            if (flSettings.TagConfigs != null)
            {
                foreach (var config in flSettings.TagConfigs)
                {
                    tagConfigs.Add(config);
                }
            }
            
            // 필터 체크박스 초기화 (전체 표시)
            if (chkShowOnlyEnabled != null)
            {
                chkShowOnlyEnabled.IsChecked = false;
            }
            
            // ItemsSource 재설정으로 필터 초기화
            dgTags.ItemsSource = null;
            dgTags.ItemsSource = tagConfigs;

            // 필드 설정 로드 (Order로 정렬)
            fieldConfigs.Clear();
            if (flSettings.FieldConfigs != null)
            {
                foreach (var config in flSettings.FieldConfigs.OrderBy(f => f.Order))
                {
                    fieldConfigs.Add(config);
                }
                // Order 재정렬 (1부터 연속)
                for (int i = 0; i < fieldConfigs.Count; i++)
                {
                    fieldConfigs[i].Order = i + 1;
                }
            }
            
            // 필터 체크박스 초기화 (전체 표시)
            if (chkShowOnlyColumnFields != null)
            {
                chkShowOnlyColumnFields.IsChecked = false;
            }
            
            // ItemsSource 재설정으로 필터 초기화
            dgFields.ItemsSource = null;
            dgFields.ItemsSource = fieldConfigs;

            // 탭 설정 로드
            tabConfigs.Clear();
            var tabs = flSettings.TabSettings?.Tabs ?? new List<FLTabConfig>();
            foreach (var tab in tabs)
            {
                tabConfigs.Add(tab);
            }
            
            // ItemsSource 재설정
            listBoxTabs.ItemsSource = null;
            listBoxTabs.ItemsSource = tabConfigs;

            if (tabConfigs.Count > 0)
                listBoxTabs.SelectedIndex = 0;
                
            System.Diagnostics.Debug.WriteLine($"✅ FL LoadPresetToUI 완료: tagConfigs={tagConfigs.Count}, fieldConfigs={fieldConfigs.Count}, tabConfigs={tabConfigs.Count}");
        }

        private void SaveUIToPreset()
        {
            // 필드를 Order로 정렬하고 Order를 연속적으로 재할당
            var sortedFields = fieldConfigs.OrderBy(f => f.Order).ToList();
            for (int i = 0; i < sortedFields.Count; i++)
            {
                sortedFields[i].Order = i + 1;
            }

            // 태그도 Order로 정렬
            var sortedTags = tagConfigs.OrderBy(t => t.Order).ToList();
            for (int i = 0; i < sortedTags.Count; i++)
            {
                sortedTags[i].Order = i + 1;
            }

            currentPreset.FLSettings = new FLPresetSettings
            {
                TagConfigs = sortedTags,
                FieldConfigs = sortedFields,
                TabSettings = new FLTabSettings
                {
                    Tabs = tabConfigs.ToList(),
                    LastSelectedTabIndex = listBoxTabs.SelectedIndex >= 0 ? listBoxTabs.SelectedIndex : 0
                }
            };
            
            System.Diagnostics.Debug.WriteLine($"💾 FL SaveUIToPreset: 태그={sortedTags.Count}개, 필드={sortedFields.Count}개, 탭={tabConfigs.Count}개");
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToPreset();
            UnifiedPresetManager.SavePreset(currentPreset);
            System.Diagnostics.Debug.WriteLine($"💾 FL 프리셋 저장 완료: {currentPreset.Name}");
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

        private void TxtPresetName_TextChanged(object sender, TextChangedEventArgs e)
        {
            btnClearPresetName.Visibility = string.IsNullOrEmpty(txtPresetName.Text) 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }

        private void BtnClearPresetName_Click(object sender, RoutedEventArgs e)
        {
            txtPresetName.Clear();
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
            itemsSelectedTags.ItemsSource = selectedTab.SelectedTagNames;
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
                SelectedTagNames = new List<string>()
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

        private void BtnAddTag_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            // 태그 선택 다이얼로그 (이미 선택된 태그 전달)
            var availableTags = tagConfigs.Where(t => t.IsEnabled).Select(t => t.TagName).ToList();
            var selectWindow = new FLTagSelectWindow(availableTags, selectedTab.SelectedTagNames);
            selectWindow.Owner = this;

            if (selectWindow.ShowDialog() == true)
            {
                // 기존 목록을 새 선택으로 교체
                selectedTab.SelectedTagNames.Clear();
                selectedTab.SelectedTagNames.AddRange(selectWindow.SelectedTags);

                itemsSelectedTags.ItemsSource = null;
                itemsSelectedTags.ItemsSource = selectedTab.SelectedTagNames;
                listBoxTabs.Items.Refresh();
            }
        }

        private void BtnRemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var tagName = button?.Tag as string;
            if (tagName != null)
            {
                selectedTab.SelectedTagNames.Remove(tagName);
                itemsSelectedTags.ItemsSource = null;
                itemsSelectedTags.ItemsSource = selectedTab.SelectedTagNames;
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

        private void BtnFieldBulkChange_Click(object sender, RoutedEventArgs e)
        {
            var selectedType = (cboFieldBulkChange.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(selectedType))
            {
                MessageBox.Show("변경할 타입을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var checkedItems = fieldConfigs.Where(f => f.IsSelected).ToList();
            if (checkedItems.Count == 0)
            {
                MessageBox.Show("변경할 항목을 체크해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool showAsColumn = selectedType == "Column";

            foreach (var item in checkedItems)
            {
                item.ShowAsColumn = showAsColumn;
                item.IsSelected = false;
            }

            dgFields.Items.Refresh();
            MessageBox.Show($"{checkedItems.Count}개 항목이 '{selectedType}'(으)로 변경되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnEditValueMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FLFieldConfig field)
            {
                // ValueMappingPopup 사용 (이벤트뷰어와 동일)
                var popup = new ValueMappingPopup(field.ValueMapping);
                popup.Owner = this;

                if (popup.ShowDialog() == true)
                {
                    field.ValueMapping = popup.ResultMapping;
                    dgFields.Items.Refresh();
                }
            }
        }

        #endregion

        #region 버튼 이벤트

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            SaveUIToPreset();
            UnifiedPresetManager.SavePreset(currentPreset);
            UnifiedPresetManager.CurrentPreset = currentPreset;
            
            // 현재 설정 저장
            AppSettingsManager.Settings.CurrentPresetName = currentPreset.Name;
            AppSettingsManager.SaveCurrent();
            
            System.Diagnostics.Debug.WriteLine($"✅ FL 프리셋 적용 완료: {currentPreset.Name}");
            
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
