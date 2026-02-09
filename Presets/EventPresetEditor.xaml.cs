using FACTOVA_MessageLogViewer.Models;
using FACTOVA_MessageLogViewer.Popup;
using FACTOVA_MessageLogViewer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer.Presets
{
    public partial class EventPresetEditor : Window
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

        /// <summary>
        /// 기본 생성자 (로그 파일 없이 열기)
        /// </summary>
        public EventPresetEditor() : this("", null)
        {
        }

        public EventPresetEditor(string logFilePath, string? selectedPresetName = null)
        {
            InitializeComponent();
            this.logFilePath = logFilePath;
            this.initialPresetName = selectedPresetName;

            dgFields.ItemsSource = fieldItems;

            LoadPresetList();

            // 선택된 프리셋 로드 후 필드 표시
            LoadSelectedPresetFields();
            LoadTabSettings();
        }

        #region 프리셋 관리

        private void LoadPresetList()
        {
            isLoadingPreset = true;
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");

            // 통합 프리셋 목록도 가져오기
            foreach (var preset in UnifiedPresetManager.GetPresetNames())
            {
                if (!cboPresets.Items.Contains(preset))
                    cboPresets.Items.Add(preset);
            }

            // 기존 프리셋 목록도 가져오기
            foreach (var preset in ColumnSettingsManager.GetPresetNames())
            {
                if (!cboPresets.Items.Contains(preset))
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

        /// <summary>
        /// 선택된 프리셋의 필드 로드
        /// </summary>
        private void LoadSelectedPresetFields()
        {
            var selected = cboPresets.SelectedItem?.ToString();
            System.Diagnostics.Debug.WriteLine($"📂 LoadSelectedPresetFields: '{selected}'");

            ColumnSettings? settings = null;

            if (string.IsNullOrEmpty(selected) || selected == "Default")
            {
                settings = ColumnSettingsManager.CurrentSettings;
                System.Diagnostics.Debug.WriteLine($"   CurrentSettings 사용 - Fields: {settings?.Fields?.Count ?? 0}개");
            }
            else
            {
                settings = ColumnSettingsManager.LoadPreset(selected);
                System.Diagnostics.Debug.WriteLine($"   LoadPreset('{selected}') 결과 - Fields: {settings?.Fields?.Count ?? 0}개");
            }

            if (settings != null && settings.Fields != null && settings.Fields.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"   ✅ 프리셋에서 {settings.Fields.Count}개 필드 로드");
                ApplySettingsToGrid(settings);
                ApplyTabSettingsFromSettings(settings);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ 프리셋 필드 없음 (settings={settings != null}, Fields={settings?.Fields?.Count ?? 0})");
                System.Diagnostics.Debug.WriteLine($"   로그 파일 분석 시도: '{logFilePath}'");
                AnalyzeAndLoadFields();

                // 필터 설정은 현재 설정에서 로드
                var currentSettings = ColumnSettingsManager.CurrentSettings;
                txtExcludedMsgIds.Text = currentSettings.ExcludedMsgIds ?? "";
                txtIncludeKeywords.Text = currentSettings.IncludeKeywords ?? "";
            }
        }



        #endregion

        #region 컬럼 설정

        private void AnalyzeAndLoadFields()
        {
            fieldItems.Clear();

            // 로그 파일이 있으면 분석, 없으면 빈 결과
            var analysisResults = string.IsNullOrEmpty(logFilePath) || !System.IO.File.Exists(logFilePath)
                ? new List<FieldAnalysisResult>()
                : LogFieldAnalyzer.AnalyzeFields(logFilePath);

            var currentSettings = ColumnSettingsManager.CurrentSettings;
            System.Diagnostics.Debug.WriteLine($"📋 AnalyzeAndLoadFields: 현재 설정 필드 {currentSettings.Fields.Count}개, 분석 결과 {analysisResults.Count}개");

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

            // Add new fields from analysis (로그 파일이 있을 때만)
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

            System.Diagnostics.Debug.WriteLine($"📋 총 {fieldItems.Count}개 필드 로드됨");
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
                // Default는 현재 설정 사용 (ExcludedMsgIds 유지)
                settings = ColumnSettingsManager.CurrentSettings;
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
            System.Diagnostics.Debug.WriteLine($"📂 ApplySettingsToGrid - 프리셋 필드 로드: {settings.Fields.Count}개");

            // 프리셋의 모든 필드를 로드 (기존 fieldItems 대체)
            fieldItems.Clear();

            int order = 1;
            foreach (var config in settings.Fields.OrderBy(f => f.Order))
            {
                fieldItems.Add(new FieldSettingItem
                {
                    Order = order++,
                    FieldName = config.FieldName,
                    DisplayName = config.DisplayName,
                    DisplayType = config.DisplayType,
                    ColumnWidth = config.ColumnWidth,
                    ValueMapping = config.ValueMapping,
                    VisibleInTabs = config.VisibleInTabs,
                    SampleValues = new List<string>()
                });
            }

            System.Diagnostics.Debug.WriteLine($"📂 ApplySettingsToGrid 완료 - {fieldItems.Count}개 필드 로드됨");

            // 필터 설정 로드
            txtExcludedMsgIds.Text = settings.ExcludedMsgIds ?? "";
            txtIncludeKeywords.Text = settings.IncludeKeywords ?? "";

            // DataGrid 갱신
            dgFields.ItemsSource = null;
            dgFields.ItemsSource = fieldItems;
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
                // 선택한 프리셋에 통합 프리셋으로 저장
                var unifiedPreset = UnifiedPresetManager.LoadPreset(selected) ?? new UnifiedPreset { Name = selected };
                unifiedPreset.EventSettings = settings;
                UnifiedPresetManager.SavePreset(unifiedPreset);
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
            settings.Name = name;

            // 새 통합 프리셋 생성하여 저장
            var unifiedPreset = new UnifiedPreset
            {
                Name = name,
                EventSettings = settings
            };
            UnifiedPresetManager.SavePreset(unifiedPreset);

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

            var settings = new ColumnSettings
            {
                Name = presetName,
                Fields = fieldItems.Select((item, index) => new FieldConfig
                {
                    FieldName = item.FieldName,
                    DisplayName = item.DisplayName,
                    DisplayType = item.DisplayType,
                    ColumnWidth = item.ColumnWidth,
                    ValueMapping = item.ValueMapping,
                    Order = index,
                    VisibleInTabs = item.VisibleInTabs
                }).ToList(),
                TabSettings = new TabSettings
                {
                    Tabs = tabs.ToList(),
                    LastSelectedTabIndex = 0
                },
                FontSize = ColumnSettingsManager.CurrentSettings.FontSize,
                ExcludedMsgIds = txtExcludedMsgIds.Text.Trim(),
                IncludeKeywords = txtIncludeKeywords.Text
            };

            return settings;
        }

        /// <summary>
        /// Tab Settings의 DisplayNames를 Column Settings의 ValueMapping으로 동기화
        /// </summary>
        private void SyncDisplayNamesToValueMapping()
        {
            foreach (var tab in tabs)
            {
                foreach (var group in tab.ConditionGroups)
                {
                    foreach (var condition in group.Conditions)
                    {
                        if (string.IsNullOrEmpty(condition.FieldName) ||
                            string.IsNullOrEmpty(condition.Value) ||
                            string.IsNullOrEmpty(condition.DisplayNames))
                        {
                            continue;
                        }

                        var fieldItem = fieldItems.FirstOrDefault(f => f.FieldName == condition.FieldName);
                        if (fieldItem == null) continue;

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
                            if (string.IsNullOrEmpty(fieldItem.ValueMapping) ||
                                mappings.Count > fieldItem.ValueMapping.Split(',').Length)
                            {
                                fieldItem.ValueMapping = newMapping;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 컬럼 표시만 체크박스 - Column 타입만 필터링
        /// </summary>
        private ObservableCollection<FieldSettingItem> allFieldItems = new();
        
        private void ChkColumnOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (chkColumnOnly.IsChecked == true)
            {
                // 전체 목록 백업
                if (allFieldItems.Count == 0)
                {
                    allFieldItems = new ObservableCollection<FieldSettingItem>(fieldItems);
                }
                
                // Column 타입만 필터링
                var columnOnly = allFieldItems.Where(f => f.DisplayType == FieldDisplayType.Column).ToList();
                fieldItems.Clear();
                foreach (var item in columnOnly)
                {
                    fieldItems.Add(item);
                }
            }
            else
            {
                // 전체 목록 복원
                if (allFieldItems.Count > 0)
                {
                    fieldItems.Clear();
                    foreach (var item in allFieldItems)
                    {
                        fieldItems.Add(item);
                    }
                    allFieldItems.Clear();
                }
            }
            
            dgFields.ItemsSource = null;
            dgFields.ItemsSource = fieldItems;
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

        private void BtnReanalyze_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // MainWindow 찾기
                var mainWindow = Application.Current.MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    MessageBox.Show("메인 윈도우를 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // EventLogViewerControl 찾기
                var eventControl = mainWindow.FindName("eventLogViewer") as EventLogViewerControl;
                if (eventControl == null)
                {
                    MessageBox.Show("EVENT 로그 컨트롤을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Reflection을 사용하여 logEntries에 접근
                var logEntriesField = typeof(EventLogViewerControl).GetField("logEntries", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (logEntriesField == null)
                {
                    MessageBox.Show("로그 데이터에 접근할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var logEntries = logEntriesField.GetValue(eventControl) as System.Collections.IEnumerable;
                if (logEntries == null)
                {
                    MessageBox.Show("로드된 EVENT 로그가 없습니다.\n먼저 로그를 로드해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 모든 로그에서 필드 추출
                var fieldSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sampleValues = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                int count = 0;

                foreach (var entry in logEntries)
                {
                    count++;
                    
                    // LogEntry의 Fields 속성에서 필드 추출
                    if (entry is LogEntry logEntry && logEntry.Fields != null)
                    {
                        foreach (var kvp in logEntry.Fields)
                        {
                            fieldSet.Add(kvp.Key);
                            if (!sampleValues.ContainsKey(kvp.Key))
                                sampleValues[kvp.Key] = new List<string>();
                            if (sampleValues[kvp.Key].Count < 3 && !string.IsNullOrWhiteSpace(kvp.Value))
                                sampleValues[kvp.Key].Add(kvp.Value);
                        }
                    }
                }

                if (count == 0)
                {
                    MessageBox.Show("로드된 EVENT 로그가 없습니다.\n먼저 로그를 로드해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 기존 필드를 모두 지우고 새 로그 기준으로 완전히 새로 구성
                fieldItems.Clear();
                int order = 1;

                foreach (var field in fieldSet.OrderBy(f => f))
                {
                    fieldItems.Add(new FieldSettingItem
                    {
                        Order = order++,
                        FieldName = field,
                        DisplayName = field,
                        DisplayType = FieldDisplayType.Summary,
                        ColumnWidth = 100,
                        VisibleInTabs = null,
                        SampleValues = sampleValues.ContainsKey(field) ? sampleValues[field] : new List<string>()
                    });
                }

                // DataGrid 완전 초기화 (ItemsSource 재설정)
                dgFields.ItemsSource = null;
                dgFields.ItemsSource = fieldItems;

                MessageBox.Show($"분석 완료!\n- 로그 수: {count}개\n- 발견된 필드: {fieldItems.Count}개",
                    "재분석", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"필드 분석 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                            DisplayNames = c.DisplayNames,
                            IsKeywordSearch = c.IsKeywordSearch
                        }).ToList() ?? new List<TabFilterCondition>(),
                        ConditionGroups = tab.ConditionGroups?.Select(g => new ConditionGroup
                        {
                            Name = g.Name,
                            Conditions = g.Conditions?.Select(c => new TabFilterCondition
                            {
                                FieldName = c.FieldName,
                                Value = c.Value,
                                ExactMatch = c.ExactMatch,
                                DisplayNames = c.DisplayNames,
                                IsKeywordSearch = c.IsKeywordSearch
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

        #region 조건 필드 콤보박스

        /// <summary>
        /// 조건 필드 콤보박스 선택 변경 - 명시적으로 FieldName 업데이트
        /// </summary>
        private void ConditionFieldComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.DataContext is TabFilterCondition condition)
            {
                if (comboBox.SelectedItem is string selectedField)
                {
                    condition.FieldName = selectedField;
                    System.Diagnostics.Debug.WriteLine($"🔧 필드 선택: {selectedField}");
                }
            }
        }

        /// <summary>
        /// 조건 필드 콤보박스 포커스 잃을 때 - 텍스트 입력값 확정
        /// </summary>
        private void ConditionFieldComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.DataContext is TabFilterCondition condition)
            {
                // 텍스트 입력값 확정
                if (!string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    condition.FieldName = comboBox.Text;
                    System.Diagnostics.Debug.WriteLine($"🔧 필드 확정: {comboBox.Text}");
                }
            }
        }

        #endregion

        #region 프리셋 폴더/삭제

        /// <summary>
        /// 프리셋 삭제
        /// </summary>
        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var selectedPreset = cboPresets.SelectedItem?.ToString();

            if (string.IsNullOrEmpty(selectedPreset) || selectedPreset == "Default")
            {
                MessageBox.Show("Default 프리셋은 삭제할 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"'{selectedPreset}' 프리셋을 삭제하시겠습니까?\n\n이 작업은 되돌릴 수 없습니다.",
                "프리셋 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // 통합 프리셋 삭제 시도
                bool deleted = UnifiedPresetManager.DeletePreset(selectedPreset);

                // 기존 프리셋도 삭제 시도
                if (!deleted)
                {
                    deleted = ColumnSettingsManager.DeletePreset(selectedPreset);
                }

                if (deleted)
                {
                    MessageBox.Show($"'{selectedPreset}' 프리셋이 삭제되었습니다.", "삭제 완료", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 프리셋 목록 새로고침 후 Default 선택
                    LoadPresetList();
                    cboPresets.SelectedIndex = 0;
                }
                else
                {
                    MessageBox.Show("프리셋 파일을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프리셋 삭제 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 프리셋 폴더 열기
        /// </summary>
        private void BtnOpenPresetFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var presetFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");

                // 폴더가 없으면 생성
                if (!Directory.Exists(presetFolder))
                {
                    Directory.CreateDirectory(presetFolder);
                }

                // 파일 탐색기로 열기
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = presetFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더를 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            // 선택된 프리셋에 저장 (적용 시 자동 저장)
            var selected = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selected) || selected == "Default")
            {
                // Default 선택 시 현재 설정에 저장
                ColumnSettingsManager.SaveCurrentSettings(settings);
            }
            else
            {
                // 선택한 프리셋에 통합 프리셋으로 저장
                var unifiedPreset = UnifiedPresetManager.LoadPreset(selected) ?? new UnifiedPreset { Name = selected };
                unifiedPreset.EventSettings = settings;
                UnifiedPresetManager.SavePreset(unifiedPreset);
            }

            ColumnSettingsManager.CurrentSettings = settings;
            
            // 현재 프리셋 이름 저장
            AppSettingsManager.Settings.CurrentPresetName = selected ?? "Default";
            AppSettingsManager.SaveCurrent();

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

                    // 편집 모드 종료 후 DataGrid 전체 갱신
                    dgFields.CommitEdit();
                    dgFields.CommitEdit();
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
                    
                    // 편집 모드 종료 후 새로고침
                    dgFields.CommitEdit();
                    dgFields.CommitEdit();
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
