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
                    ValueMapping = config.ValueMapping,
                    VisibleInTabs = config.VisibleInTabs,  // ✅ 추가: 탭별 표시 설정 로드!
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
                        VisibleInTabs = null,  // ✅ 추가: 새 필드는 전체 탭
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
            System.Diagnostics.Debug.WriteLine($"📂 ApplySettingsToGrid - 설정 로드 시작:");
            
            foreach (var item in fieldItems)
            {
                var config = settings.Fields.FirstOrDefault(f => f.FieldName == item.FieldName);
                if (config != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {item.FieldName}:");
                    System.Diagnostics.Debug.WriteLine($"      config.VisibleInTabs = {config.VisibleInTabs?.Count ?? 0}개");
                    if (config.VisibleInTabs != null)
                    {
                        foreach (var tab in config.VisibleInTabs)
                        {
                            System.Diagnostics.Debug.WriteLine($"        * {tab}");
                        }
                    }
                    
                    item.DisplayName = config.DisplayName;
                    item.DisplayType = config.DisplayType;
                    item.ColumnWidth = config.ColumnWidth;
                    item.ValueMapping = config.ValueMapping;
                    item.VisibleInTabs = config.VisibleInTabs;  // 탭별 표시 설정 로드
                    
                    System.Diagnostics.Debug.WriteLine($"      item.VisibleInTabs = {item.VisibleInTabs?.Count ?? 0}개");
                    System.Diagnostics.Debug.WriteLine($"      DisplayText = {item.VisibleTabsDisplayText}");
                    
                    // UI 강제 갱신
                    item.RefreshDisplay();
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"📂 ApplySettingsToGrid 완료 - DataGrid 강제 갱신");
            
            // 강력한 갱신: ItemsSource를 다시 설정
            var items = dgFields.ItemsSource;
            dgFields.ItemsSource = null;
            dgFields.ItemsSource = items;
            dgFields.UpdateLayout();
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

            // Tab Settings의 DisplayNames를 Column Settings의 ValueMapping으로 동기화
            SyncDisplayNamesToValueMapping();

            // 현재 선택된 프리셋 이름 사용
            var presetName = cboPresets.SelectedItem?.ToString() ?? "Default";

            System.Diagnostics.Debug.WriteLine($"💾 CreateSettingsFromAll - 설정 저장 시작:");
            
            var settings = new ColumnSettings
            {
                Name = presetName,
                Fields = fieldItems.Select((item, index) => 
                {
                    System.Diagnostics.Debug.WriteLine($"  - {item.FieldName}: VisibleInTabs = {item.VisibleInTabs?.Count ?? 0}개");
                    if (item.VisibleInTabs != null)
                    {
                        foreach (var tab in item.VisibleInTabs)
                        {
                            System.Diagnostics.Debug.WriteLine($"      * {tab}");
                        }
                    }
                    
                    return new FieldConfig
                    {
                        FieldName = item.FieldName,
                        DisplayName = item.DisplayName,
                        DisplayType = item.DisplayType,
                        ColumnWidth = item.ColumnWidth,
                        ValueMapping = item.ValueMapping,
                        Order = index,
                        VisibleInTabs = item.VisibleInTabs  // 탭별 표시 설정 저장
                    };
                }).ToList(),
                TabSettings = new TabSettings
                {
                    Tabs = tabs.ToList(),
                    LastSelectedTabIndex = 0
                },
                FontSize = ColumnSettingsManager.CurrentSettings.FontSize
            };
            
            System.Diagnostics.Debug.WriteLine($"💾 CreateSettingsFromAll 완료");
            return settings;
        }

        /// <summary>
        /// Tab Settings의 DisplayNames를 Column Settings의 ValueMapping으로 동기화
        /// </summary>
        private void SyncDisplayNamesToValueMapping()
        {
            System.Diagnostics.Debug.WriteLine("=== SyncDisplayNamesToValueMapping 시작 ===");
            
            foreach (var tab in tabs)
            {
                System.Diagnostics.Debug.WriteLine($"Tab: {tab.Name}");
                
                foreach (var group in tab.ConditionGroups)
                {
                    System.Diagnostics.Debug.WriteLine($"  Group: {group.Name}");
                    
                    foreach (var condition in group.Conditions)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Condition - Field: {condition.FieldName}, Value: {condition.Value}, DisplayNames: {condition.DisplayNames}");
                        
                        if (string.IsNullOrEmpty(condition.FieldName) || 
                            string.IsNullOrEmpty(condition.Value) || 
                            string.IsNullOrEmpty(condition.DisplayNames))
                        {
                            System.Diagnostics.Debug.WriteLine($"      -> 스킵 (빈 값)");
                            continue;
                        }

                        // 해당 필드 찾기
                        var fieldItem = fieldItems.FirstOrDefault(f => f.FieldName == condition.FieldName);
                        if (fieldItem == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"      -> 필드를 찾을 수 없음: {condition.FieldName}");
                            continue;
                        }

                        // Value와 DisplayNames를 매핑 형식으로 변환
                        // 예: Value="1,2", DisplayNames="장입,미장입" → "1:장입,2:미장입"
                        var values = condition.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                    .Select(v => v.Trim())
                                                    .ToList();
                        var displayNames = condition.DisplayNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                                 .Select(d => d.Trim())
                                                                 .ToList();

                        var mappings = new List<string>();
                        for (int i = 0; i < values.Count && i < displayNames.Count; i++)
                        {
                            mappings.Add($"{values[i]}:{displayNames[i]}");
                        }

                        if (mappings.Count > 0)
                        {
                            var newMapping = string.Join(",", mappings);
                            
                            System.Diagnostics.Debug.WriteLine($"      -> 기존 ValueMapping: '{fieldItem.ValueMapping}'");
                            System.Diagnostics.Debug.WriteLine($"      -> 새 ValueMapping: '{newMapping}'");
                            
                            // 기존 매핑이 없거나, 새 매핑이 더 많은 항목을 포함하면 업데이트
                            if (string.IsNullOrEmpty(fieldItem.ValueMapping) || 
                                mappings.Count > fieldItem.ValueMapping.Split(',').Length)
                            {
                                fieldItem.ValueMapping = newMapping;
                                System.Diagnostics.Debug.WriteLine($"      -> ValueMapping 업데이트됨!");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"      -> ValueMapping 업데이트 안함 (기존 매핑이 더 많거나 같음)");
                            }
                        }
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine("=== SyncDisplayNamesToValueMapping 완료 ===");
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

        /// <summary>
        /// 체크박스 셀 전체 클릭으로 토글
        /// </summary>
        private void CheckBoxCell_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is FieldSettingItem item)
            {
                item.IsSelected = !item.IsSelected;
                e.Handled = true;
            }
        }

        private void BtnBulkChange_Click(object sender, RoutedEventArgs e)
        {
            var selectedType = (cboBulkChange.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(selectedType))
            {
                MessageBox.Show("변경할 타입을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var checkedItems = fieldItems.Where(f => f.IsSelected).ToList();
            if (checkedItems.Count == 0)
            {
                MessageBox.Show("변경할 항목을 체크해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FieldDisplayType targetType = selectedType switch
            {
                "Column" => FieldDisplayType.Column,
                "Summary" => FieldDisplayType.Summary,
                "Hidden" => FieldDisplayType.Hidden,
                _ => FieldDisplayType.Summary
            };

            foreach (var item in checkedItems)
            {
                item.DisplayType = targetType;
                item.IsSelected = false;  // 체크박스 해제
            }

            UpdateFieldOrders();
            MessageBox.Show($"{checkedItems.Count}개 항목이 {selectedType}(으)로 변경되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 체크한 항목들의 표시할 탭 일괄 설정
        /// </summary>
        private void BtnBulkSetTabs_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = fieldItems.Where(f => f.IsSelected).ToList();
            if (checkedItems.Count == 0)
            {
                MessageBox.Show("표시할 탭을 설정할 항목을 체크해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // tabs가 null이면 경고
            if (tabs == null || tabs.Count == 0)
            {
                MessageBox.Show("설정된 탭이 없습니다. 탭 설정 탭에서 탭을 먼저 설정해주세요.", 
                                "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 현재 설정의 탭 목록 가져오기
            var availableTabs = tabs.Select(t => t.Name).ToList();

            // 팝업 창 생성 (빈 선택으로 시작)
            var popup = new TabSelectionPopup(availableTabs, new List<string>());
            popup.Owner = this;
            popup.Title = "일괄 탭 설정";

            if (popup.ShowDialog() == true)
            {
                // 선택 결과 적용 (전체 탭이면 null)
                var selectedTabs = popup.SelectedTabs.Count > 0 ? popup.SelectedTabs : null;
                
                foreach (var item in checkedItems)
                {
                    item.VisibleInTabs = selectedTabs != null ? new List<string>(selectedTabs) : null;
                    item.IsSelected = false;  // 체크박스 해제
                }

                // DataGrid 갱신
                var items = dgFields.ItemsSource;
                dgFields.ItemsSource = null;
                dgFields.ItemsSource = items;

                string tabsText = selectedTabs == null ? "전체 탭" : string.Join(", ", selectedTabs);
                MessageBox.Show($"{checkedItems.Count}개 항목의 표시할 탭이 [{tabsText}](으)로 설정되었습니다.", 
                                "완료", MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>
        /// 입력한 순번대로 정렬
        /// </summary>
        private void BtnApplyOrder_Click(object sender, RoutedEventArgs e)
        {
            // 입력된 순번대로 정렬
            var sortedItems = fieldItems.OrderBy(f => f.Order).ToList();
            
            fieldItems.Clear();
            foreach (var item in sortedItems)
            {
                fieldItems.Add(item);
            }
            
            // 순번 재정렬 (1부터 연속으로)
            UpdateFieldOrders();
            
            MessageBox.Show("순번대로 정렬되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 표시타입으로 정렬 (선택한 타입을 맨 위로)
        /// </summary>
        private void BtnSortByType_Click(object sender, RoutedEventArgs e)
        {
            var selectedType = (cboSortType.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(selectedType))
            {
                MessageBox.Show("정렬할 타입을 선택해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 선택한 타입을 맨 위로, 나머지는 기존 순서 유지
            var sortedItems = fieldItems
                .OrderByDescending(f => f.DisplayTypeString == selectedType)  // 선택한 타입 먼저
                .ThenBy(f => f.DisplayTypeString switch  // Column > Summary > Hidden 순
                {
                    "Column" => 0,
                    "Summary" => 1,
                    "Hidden" => 2,
                    _ => 3
                })
                .ThenBy(f => f.Order)  // 기존 순서 유지
                .ToList();

            fieldItems.Clear();
            foreach (var item in sortedItems)
            {
                fieldItems.Add(item);
            }

            UpdateFieldOrders();
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
                            ExactMatch = c.ExactMatch,
                            DisplayNames = c.DisplayNames
                        }).ToList() ?? new List<TabFilterCondition>(),
                        ConditionGroups = tab.ConditionGroups?.Select(g => new ConditionGroup
                        {
                            Name = g.Name,
                            Conditions = g.Conditions?.Select(c => new TabFilterCondition
                            {
                                FieldName = c.FieldName,
                                Value = c.Value,
                                ExactMatch = c.ExactMatch,
                                DisplayNames = c.DisplayNames
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

        /// <summary>
        /// 탭 선택 버튼 클릭
        /// </summary>
        private void BtnSelectTabs_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is FieldSettingItem fieldItem)
            {
                // tabs가 null이면 경고
                if (tabs == null || tabs.Count == 0)
                {
                    MessageBox.Show("설정된 탭이 없습니다. Tab Settings 탭에서 탭을 먼저 설정해주세요.", 
                                    "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 현재 설정의 탭 목록 가져오기
                var availableTabs = tabs.Select(t => t.Name).ToList();
                
                if (availableTabs.Count == 0)
                {
                    MessageBox.Show("설정된 탭이 없습니다. Tab Settings 탭에서 탭을 먼저 설정해주세요.", 
                                    "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 현재 선택된 탭 목록 (디버그)
                System.Diagnostics.Debug.WriteLine($"🔍 팝업 열기 전 - VisibleInTabs: {fieldItem.VisibleInTabs?.Count ?? 0}개");
                if (fieldItem.VisibleInTabs != null)
                {
                    foreach (var tab in fieldItem.VisibleInTabs)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {tab}");
                    }
                }
                
                var currentSelection = fieldItem.VisibleInTabs ?? new List<string>();
                
                // 팝업 창 생성
                var popup = new TabSelectionPopup(availableTabs, currentSelection);
                popup.Owner = this;
                
                if (popup.ShowDialog() == true)
                {
                    // 선택 결과 적용
                    var selectedTabs = popup.SelectedTabs.Count > 0 ? popup.SelectedTabs : null;
                    
                    System.Diagnostics.Debug.WriteLine($"✅ 팝업 확인 후 - 선택된 탭: {selectedTabs?.Count ?? 0}개");
                    if (selectedTabs != null)
                    {
                        foreach (var tab in selectedTabs)
                        {
                            System.Diagnostics.Debug.WriteLine($"  - {tab}");
                        }
                    }
                    
                    fieldItem.VisibleInTabs = selectedTabs;
                    
                    System.Diagnostics.Debug.WriteLine($"✅ FieldItem 업데이트 후: {fieldItem.VisibleTabsDisplayText}");
                    
                    // DataGrid 전체 갱신
                    dgFields.Items.Refresh();
                }
            }
        }

        /// <summary>
        /// 값 매핑 편집 버튼 클릭
        /// </summary>
        private void BtnEditValueMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is FieldSettingItem fieldItem)
            {
                var popup = new ValueMappingPopup(fieldItem.ValueMapping);
                popup.Owner = this;
                
                if (popup.ShowDialog() == true)
                {
                    fieldItem.ValueMapping = popup.ResultMapping;
                    dgFields.Items.Refresh();
                }
            }
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
        private bool _isSelected = false;
        private string _valueMapping = "";

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

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string ValueMapping
        {
            get => _valueMapping;
            set 
            { 
                _valueMapping = value; 
                OnPropertyChanged(nameof(ValueMapping)); 
                OnPropertyChanged(nameof(ValueMappingDisplayText));
            }
        }

        /// <summary>
        /// 값 매핑 UI 표시용 텍스트
        /// </summary>
        public string ValueMappingDisplayText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_valueMapping))
                    return "(없음)";
                
                var pairs = _valueMapping.Split(',');
                if (pairs.Length > 2)
                    return $"{pairs.Length}개 매핑";
                
                // 짧게 표시: 1→자동, 2→수동
                var displayPairs = pairs.Take(2).Select(p =>
                {
                    var parts = p.Split(':');
                    return parts.Length == 2 ? $"{parts[0].Trim()}→{parts[1].Trim()}" : p;
                });
                return string.Join(", ", displayPairs);
            }
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

        private List<string>? _visibleInTabs = null;
        
        /// <summary>
        /// 이 컬럼을 표시할 탭 목록
        /// </summary>
        public List<string>? VisibleInTabs
        {
            get => _visibleInTabs;
            set
            {
                System.Diagnostics.Debug.WriteLine($"🔧 FieldSettingItem.VisibleInTabs setter 호출: {FieldName}");
                System.Diagnostics.Debug.WriteLine($"   이전 값: {_visibleInTabs?.Count ?? 0}개");
                System.Diagnostics.Debug.WriteLine($"   새 값: {value?.Count ?? 0}개");
                
                _visibleInTabs = value;
                OnPropertyChanged(nameof(VisibleInTabs));
                OnPropertyChanged(nameof(VisibleTabsDisplayText));
                
                System.Diagnostics.Debug.WriteLine($"   DisplayText: {VisibleTabsDisplayText}");
            }
        }

        /// <summary>
        /// UI 표시용 텍스트
        /// </summary>
        public string VisibleTabsDisplayText
        {
            get
            {
                if (_visibleInTabs == null || _visibleInTabs.Count == 0)
                    return "전체 탭";
                if (_visibleInTabs.Count > 2)
                    return $"{_visibleInTabs[0]} 외 {_visibleInTabs.Count - 1}개";
                return string.Join(", ", _visibleInTabs);
            }
        }

        public System.Collections.Generic.List<string> SampleValues { get; set; } = new();
        
        public string SamplePreview => SampleValues.Count > 0 
            ? string.Join(", ", SampleValues.Take(3)) 
            : "(no value)";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        
        /// <summary>
        /// UI 강제 갱신용 public 메서드
        /// </summary>
        public void RefreshDisplay()
        {
            OnPropertyChanged(nameof(VisibleTabsDisplayText));
        }
    }
}
