using FACTOVA_MessageLogViewer.Models;
using FACTOVA_MessageLogViewer.Popup;
using FACTOVA_MessageLogViewer.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;


namespace FACTOVA_MessageLogViewer.Presets
{
    public partial class FLPresetEditor : Window
    {
        private ObservableCollection<FLTagConfig> tagConfigs = new();
        private ObservableCollection<FLFieldConfig> fieldConfigs = new();
        private ObservableCollection<FLTabConfig> tabConfigs = new();
        private FLTabConfig? selectedTab;
        private UnifiedPreset currentPreset = UnifiedPreset.CreateDefault();
        private bool isInitializing = true; // 초기화 중 플래그

        // 드래그 앤 드롭 관련
        private Point dragStartPoint;
        private FLTagItem? draggedTagItem;
        private FLTagGroup? draggedTagGroup;

        // 그룹 관리
        private ObservableCollection<string> availableGroupNames = new() { "", "기본" };
        public ObservableCollection<string> AvailableGroupNames => availableGroupNames;

        // 현재 로드된 F/L 로그에서 태그 추출을 위한 콜백
        public Func<IEnumerable<FLLogEntry>>? GetCurrentLogEntries { get; set; }

        public FLPresetEditor(string presetName = "Default")
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
            
            // 그룹 목록 UI 업데이트
            itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
        }

        /// <summary>
        /// 패턴 자동 분석 버튼 클릭
        /// </summary>
        private void BtnAutoAnalyzePattern_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentLogEntries == null)
            {
                MessageBox.Show("현재 로드된 F/L 로그가 없습니다.\n먼저 F/L 뷰어에서 로그를 로드해주세요.", 
                    "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var entries = GetCurrentLogEntries().ToList();
            if (entries.Count == 0)
            {
                MessageBox.Show("분석할 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 패턴 분석 수행
            var analysisResult = TagPatternAnalyzer.AnalyzeEntries(entries);

            if (analysisResult.Tabs.Count == 0)
            {
                MessageBox.Show("분석 가능한 패턴을 찾지 못했습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 미리보기 팝업 표시 (사용자가 선택/해제 가능)
            var previewPopup = new PatternAnalysisPreviewPopup(analysisResult);
            previewPopup.Owner = this;

            if (previewPopup.ShowDialog() != true)
                return;

            // 팝업에서 필터링된 결과 사용
            var filteredResult = previewPopup.Result;

            // 탭 설정 적용
            tabConfigs.Clear();
            foreach (var tab in filteredResult.Tabs)
            {
                tabConfigs.Add(tab);
            }

            // 태그 설명도 업데이트 (기존 유지, 새로 추가)
            var existingTags = tagConfigs.ToDictionary(t => t.TagName, t => t);
            int order = tagConfigs.Count;

            foreach (var tagConfig in filteredResult.TagConfigs)
            {
                if (!existingTags.ContainsKey(tagConfig.TagName))
                {
                    tagConfig.Order = ++order;
                    tagConfigs.Add(tagConfig);
                }
                else
                {
                    // 기존 태그에 설명이 없으면 자동 생성된 설명 적용
                    var existing = existingTags[tagConfig.TagName];
                    if (string.IsNullOrEmpty(existing.DisplayName) && !string.IsNullOrEmpty(tagConfig.DisplayName))
                    {
                        existing.DisplayName = tagConfig.DisplayName;
                    }
                }
            }

            // UI 갱신
            listBoxTabs.ItemsSource = null;
            listBoxTabs.ItemsSource = tabConfigs;
            if (tabConfigs.Count > 0)
                listBoxTabs.SelectedIndex = 0;

            dgTags.Items.Refresh();

            MessageBox.Show(filteredResult.Summary, "자동 분석 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var newGroup = new FLTagGroup
            {
                GroupName = $"그룹 {selectedTab.TagGroups.Count + 1}"
            };

            selectedTab.TagGroups.Add(newGroup);
            itemsConditionGroups.ItemsSource = null;
            itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
        }

        private void BtnRemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var group = button?.Tag as FLTagGroup;
            if (group != null)
            {
                if (selectedTab.TagGroups.Count == 1)
                {
                    MessageBox.Show("최소 1개의 그룹이 필요합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"그룹 '{group.GroupName}'을(를) 삭제하시겠습니까?",
                    "그룹 삭제 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    selectedTab.TagGroups.Remove(group);
                    itemsConditionGroups.ItemsSource = null;
                    itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                }
            }
        }

        private void BtnAddTagToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var group = button?.Tag as FLTagGroup;
            if (group == null) return;

            // 태그 선택 다이얼로그
            var availableTags = tagConfigs.Where(t => t.IsEnabled).Select(t => t.TagName).ToList();
            var alreadySelected = group.Tags.Select(t => t.TagName).ToList();
            var selectWindow = new FLTagSelectWindow(availableTags, alreadySelected);
            selectWindow.Owner = this;

            if (selectWindow.ShowDialog() == true)
            {
                // 새로 선택된 태그들을 그룹에 추가
                var existingTagNames = group.Tags.Select(t => t.TagName).ToHashSet();
                
                foreach (var tagName in selectWindow.SelectedTags)
                {
                    if (!existingTagNames.Contains(tagName))
                    {
                        group.Tags.Add(new FLTagItem 
                        { 
                            TagName = tagName,
                            GroupName = group.GroupName
                        });
                    }
                }

                // UI 갱신
                itemsConditionGroups.ItemsSource = null;
                itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                listBoxTabs.Items.Refresh();
            }
        }

        private void BtnRemoveTagFromGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var tag = button?.Tag as FLTagItem;
            if (tag == null) return;

            // 태그가 속한 그룹 찾기
            var group = selectedTab.TagGroups.FirstOrDefault(g => g.Tags.Contains(tag));
            if (group != null)
            {
                group.Tags.Remove(tag);
                
                // UI 갱신
                itemsConditionGroups.ItemsSource = null;
                itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                listBoxTabs.Items.Refresh();
            }
        }

        private void BtnMoveTagUp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var tag = button?.Tag as FLTagItem;
            if (tag == null) return;

            // 태그가 속한 그룹 찾기
            var group = selectedTab.TagGroups.FirstOrDefault(g => g.Tags.Contains(tag));
            if (group != null)
            {
                int index = group.Tags.IndexOf(tag);
                if (index > 0)
                {
                    group.Tags.RemoveAt(index);
                    group.Tags.Insert(index - 1, tag);
                    
                    // UI 갱신
                    itemsConditionGroups.ItemsSource = null;
                    itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                    listBoxTabs.Items.Refresh();
                }
            }
        }

        private void BtnMoveTagDown_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var tag = button?.Tag as FLTagItem;
            if (tag == null) return;

            // 태그가 속한 그룹 찾기
            var group = selectedTab.TagGroups.FirstOrDefault(g => g.Tags.Contains(tag));
            if (group != null)
            {
                int index = group.Tags.IndexOf(tag);
                if (index < group.Tags.Count - 1)
                {
                    group.Tags.RemoveAt(index);
                    group.Tags.Insert(index + 1, tag);
                    
                    // UI 갱신
                    itemsConditionGroups.ItemsSource = null;
                    itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                    listBoxTabs.Items.Refresh();
                }
            }
        }

        private void BtnDuplicateTagInGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var button = sender as Button;
            var tag = button?.Tag as FLTagItem;
            if (tag == null) return;

            // 태그가 속한 그룹 찾기
            var group = selectedTab.TagGroups.FirstOrDefault(g => g.Tags.Contains(tag));
            if (group != null)
            {
                // 복제본 생성 (같은 태그명, 다른 값 필터 사용 가능)
                var duplicate = new FLTagItem
                {
                    TagName = tag.TagName,
                    ValueFilter = tag.ValueFilter, // 복제 후 값 필터 변경 가능
                    GroupName = group.GroupName
                };
                
                // 현재 태그 바로 다음에 삽입
                int index = group.Tags.IndexOf(tag);
                group.Tags.Insert(index + 1, duplicate);
                
                // UI 갱신
                itemsConditionGroups.ItemsSource = null;
                itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                listBoxTabs.Items.Refresh();
            }
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
                TagGroups = new List<FLTagGroup>
                {
                    new FLTagGroup { GroupName = "기본" }
                }
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

        #region 드래그 앤 드롭

        private void TagItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
            
            // Border를 통해 FLTagItem 가져오기
            var border = sender as Border;
            if (border?.DataContext is FLTagItem tagItem)
            {
                draggedTagItem = tagItem;
                
                // 태그가 속한 그룹 찾기
                draggedTagGroup = selectedTab?.TagGroups.FirstOrDefault(g => g.Tags.Contains(tagItem));
            }
        }

        private void TagItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && draggedTagItem != null)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var border = sender as Border;
                    if (border != null)
                    {
                        var dragData = new DataObject("FLTagItem", draggedTagItem);
                        DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);
                    }
                }
            }
        }

        private void TagItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FLTagItem"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TagItem_Drop(object sender, DragEventArgs e)
        {
            if (selectedTab == null || !e.Data.GetDataPresent("FLTagItem")) return;

            var droppedTagItem = e.Data.GetData("FLTagItem") as FLTagItem;
            if (droppedTagItem == null || draggedTagGroup == null) return;

            // 드롭된 위치의 Border 찾기
            var border = sender as Border;
            if (border?.DataContext is FLTagItem targetTagItem)
            {
                // 같은 그룹 내에서만 이동
                var targetGroup = selectedTab.TagGroups.FirstOrDefault(g => g.Tags.Contains(targetTagItem));
                if (targetGroup != draggedTagGroup) return;

                int oldIndex = draggedTagGroup.Tags.IndexOf(droppedTagItem);
                int newIndex = draggedTagGroup.Tags.IndexOf(targetTagItem);

                if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                {
                    draggedTagGroup.Tags.RemoveAt(oldIndex);
                    if (newIndex > oldIndex)
                        newIndex--;
                    draggedTagGroup.Tags.Insert(newIndex, droppedTagItem);

                    // UI 갱신
                    itemsConditionGroups.ItemsSource = null;
                    itemsConditionGroups.ItemsSource = selectedTab.TagGroups;
                    listBoxTabs.Items.Refresh();
                }
            }

            draggedTagItem = null;
            draggedTagGroup = null;
        }

        #endregion

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

            // Structure 또는 CSFC 타입 로그에서 필드 추출
            var multilineEntries = entries.Where(e => e.HasMultilineData && e.Fields.Count > 0).ToList();
            if (multilineEntries.Count == 0)
            {
                MessageBox.Show("Structure/CSFC 타입의 로그가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var allFields = new Dictionary<string, string>(); // 필드명 → 샘플값

            foreach (var entry in multilineEntries)
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
