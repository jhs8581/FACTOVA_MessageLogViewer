using FACTOVA_MessageLogViewer.Helpers;
using FACTOVA_MessageLogViewer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer.Popup
{
    public partial class PatternAnalysisPreviewPopup : Window
    {
        private TagPatternAnalyzer.AnalysisResult originalResult;
        
        // 선택 상태 추적용 래퍼 클래스들
        private List<SelectableTab> selectableTabs = new();

        public TagPatternAnalyzer.AnalysisResult Result { get; private set; }

        public PatternAnalysisPreviewPopup(TagPatternAnalyzer.AnalysisResult result)
        {
            InitializeComponent();
            originalResult = result;
            Result = result;
            LoadResult();
        }

        private void LoadResult()
        {
            txtSummary.Text = originalResult.Summary;

            // 선택 가능한 래퍼로 변환
            selectableTabs.Clear();
            foreach (var tab in originalResult.Tabs)
            {
                var selectableTab = new SelectableTab(tab);
                selectableTabs.Add(selectableTab);
            }

            // TreeView 구성
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void BuildTreeView()
        {
            treeResult.Items.Clear();

            foreach (var selectableTab in selectableTabs)
            {
                var tabItem = new TreeViewItem
                {
                    Header = CreateTabHeader(selectableTab),
                    IsExpanded = true,
                    Tag = selectableTab
                };

                foreach (var selectableGroup in selectableTab.Groups)
                {
                    var groupItem = new TreeViewItem
                    {
                        Header = CreateGroupHeader(selectableGroup),
                        IsExpanded = true,
                        Tag = selectableGroup
                    };

                    foreach (var selectableTag in selectableGroup.Tags)
                    {
                        var tagItem = new TreeViewItem
                        {
                            Header = CreateTagHeader(selectableTag),
                            Tag = selectableTag
                        };
                        groupItem.Items.Add(tagItem);
                    }

                    tabItem.Items.Add(groupItem);
                }

                treeResult.Items.Add(tabItem);
            }
        }

        private StackPanel CreateTabHeader(SelectableTab tab)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var checkBox = new CheckBox
            {
                IsChecked = tab.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked += (s, e) => { tab.IsSelected = true; OnTabCheckedChanged(tab); };
            checkBox.Unchecked += (s, e) => { tab.IsSelected = false; OnTabCheckedChanged(tab); };
            panel.Children.Add(checkBox);
            
            panel.Children.Add(new TextBlock { Text = "📁 ", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            
            // 탭 이름 편집 가능한 TextBox
            var tabNameTextBox = new TextBox
            {
                Text = tab.DisplayName,
                MinWidth = 100,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Padding = new Thickness(6, 3, 6, 3),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x19, 0x76, 0xD2)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD)),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0D, 0x47, 0xA1)),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "탭 이름을 수정할 수 있습니다"
            };
            tabNameTextBox.TextChanged += (s, e) => 
            { 
                tab.DisplayName = tabNameTextBox.Text;
            };
            tabNameTextBox.GotFocus += (s, e) =>
            {
                tabNameTextBox.SelectAll();
            };
            panel.Children.Add(tabNameTextBox);
            
            panel.Children.Add(new TextBlock 
            { 
                Text = $" ({tab.Groups.Count}개 그룹)", 
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
            });
            return panel;
        }

        private StackPanel CreateGroupHeader(SelectableGroup group)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            
            var checkBox = new CheckBox
            {
                IsChecked = group.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0)
            };
            checkBox.Checked += (s, e) => { group.IsSelected = true; OnGroupCheckedChanged(group); };
            checkBox.Unchecked += (s, e) => { group.IsSelected = false; OnGroupCheckedChanged(group); };
            panel.Children.Add(checkBox);
            
            panel.Children.Add(new TextBlock { Text = "📂 ", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            
            // 그룹명 편집 가능한 TextBox
            var groupNameTextBox = new TextBox
            {
                Text = group.DisplayName,
                MinWidth = 120,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0x89, 0x7B)),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE0, 0xF2, 0xF1)),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0x69, 0x5C)),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "그룹명을 수정할 수 있습니다"
            };
            groupNameTextBox.TextChanged += (s, e) => 
            { 
                group.DisplayName = groupNameTextBox.Text;
            };
            groupNameTextBox.GotFocus += (s, e) =>
            {
                groupNameTextBox.SelectAll();
            };
            panel.Children.Add(groupNameTextBox);
            
            panel.Children.Add(new TextBlock 
            { 
                Text = $" ({group.Tags.Count}개 태그)", 
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
            });
            return panel;
        }

        private StackPanel CreateTagHeader(SelectableTag tag)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            
            var checkBox = new CheckBox
            {
                IsChecked = tag.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            checkBox.Checked += (s, e) => { tag.IsSelected = true; UpdateSelectionInfo(); };
            checkBox.Unchecked += (s, e) => { tag.IsSelected = false; UpdateSelectionInfo(); };
            panel.Children.Add(checkBox);
            
            // 순번 배지
            var orderBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE1, 0xBE, 0xE7)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 0, 8, 0)
            };
            orderBorder.Child = new TextBlock 
            { 
                Text = $"#{tag.Order}", 
                FontSize = 10, 
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x7B, 0x1F, 0xA2))
            };
            panel.Children.Add(orderBorder);
            
            // 태그명
            panel.Children.Add(new TextBlock 
            { 
                Text = tag.Tag.TagName, 
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11
            });
            
            // 값 필터
            if (!string.IsNullOrEmpty(tag.Tag.ValueFilter))
            {
                panel.Children.Add(new TextBlock 
                { 
                    Text = $" [{tag.Tag.ValueFilter}]", 
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                });
            }
            
            return panel;
        }

        private void OnTabCheckedChanged(SelectableTab tab)
        {
            // 탭 체크 변경 시 하위 그룹/태그도 같이 변경
            foreach (var group in tab.Groups)
            {
                group.IsSelected = tab.IsSelected;
                foreach (var tag in group.Tags)
                {
                    tag.IsSelected = tab.IsSelected;
                }
            }
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void OnGroupCheckedChanged(SelectableGroup group)
        {
            // 그룹 체크 변경 시 하위 태그도 같이 변경
            foreach (var tag in group.Tags)
            {
                tag.IsSelected = group.IsSelected;
            }
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void UpdateSelectionInfo()
        {
            int selectedTabs = selectableTabs.Count(t => t.IsSelected);
            int selectedGroups = selectableTabs.SelectMany(t => t.Groups).Count(g => g.IsSelected);
            int selectedTags = selectableTabs.SelectMany(t => t.Groups).SelectMany(g => g.Tags).Count(t => t.IsSelected);
            
            txtSelectionInfo.Text = $"선택: {selectedTabs}개 탭, {selectedGroups}개 그룹, {selectedTags}개 태그";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var tab in selectableTabs)
            {
                tab.IsSelected = true;
                foreach (var group in tab.Groups)
                {
                    group.IsSelected = true;
                    foreach (var tag in group.Tags)
                    {
                        tag.IsSelected = true;
                    }
                }
            }
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var tab in selectableTabs)
            {
                tab.IsSelected = false;
                foreach (var group in tab.Groups)
                {
                    group.IsSelected = false;
                    foreach (var tag in group.Tags)
                    {
                        tag.IsSelected = false;
                    }
                }
            }
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void BtnSelectGroupsOnly_Click(object sender, RoutedEventArgs e)
        {
            // 탭과 그룹만 선택, 태그는 전체 선택
            foreach (var tab in selectableTabs)
            {
                tab.IsSelected = true;
                foreach (var group in tab.Groups)
                {
                    group.IsSelected = true;
                    foreach (var tag in group.Tags)
                    {
                        tag.IsSelected = true;
                    }
                }
            }
            BuildTreeView();
            UpdateSelectionInfo();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            // 선택된 항목만으로 결과 재구성
            var filteredTabs = new List<FLTabConfig>();
            var filteredTagConfigs = new List<FLTagConfig>();
            var selectedTagNames = new HashSet<string>();

            foreach (var selectableTab in selectableTabs.Where(t => t.IsSelected))
            {
                var newTab = new FLTabConfig
                {
                    // 수정된 탭 이름 사용 (DisplayName)
                    Name = string.IsNullOrWhiteSpace(selectableTab.DisplayName) 
                        ? selectableTab.Tab.Name 
                        : selectableTab.DisplayName,
                    IsEnabled = selectableTab.Tab.IsEnabled,
                    IsIntegrated = selectableTab.Tab.IsIntegrated
                };

                foreach (var selectableGroup in selectableTab.Groups.Where(g => g.IsSelected))
                {
                    var newGroup = new FLTagGroup
                    {
                        // 수정된 그룹명 사용 (DisplayName)
                        GroupName = string.IsNullOrWhiteSpace(selectableGroup.DisplayName) 
                            ? selectableGroup.Group.GroupName 
                            : selectableGroup.DisplayName
                    };

                    int order = 1;
                    foreach (var selectableTag in selectableGroup.Tags.Where(t => t.IsSelected))
                    {
                        newGroup.Tags.Add(new FLTagItem
                        {
                            TagName = selectableTag.Tag.TagName,
                            ValueFilter = selectableTag.Tag.ValueFilter,
                            Order = order++
                        });
                        selectedTagNames.Add(selectableTag.Tag.TagName);
                    }

                    if (newGroup.Tags.Count > 0)
                    {
                        newTab.TagGroups.Add(newGroup);
                    }
                }

                if (newTab.TagGroups.Count > 0)
                {
                    filteredTabs.Add(newTab);
                }
            }

            // 선택된 태그에 해당하는 TagConfig만 포함
            foreach (var tagConfig in originalResult.TagConfigs)
            {
                if (selectedTagNames.Contains(tagConfig.TagName))
                {
                    filteredTagConfigs.Add(tagConfig);
                }
            }

            if (filteredTabs.Count == 0)
            {
                MessageBox.Show("적용할 항목이 없습니다.\n최소 1개 이상의 탭/그룹/태그를 선택해주세요.", 
                    "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 결과 업데이트
            Result = new TagPatternAnalyzer.AnalysisResult
            {
                Tabs = filteredTabs,
                TagConfigs = filteredTagConfigs,
                Summary = $"적용 완료: {filteredTabs.Count}개 탭, {filteredTabs.Sum(t => t.TagGroups.Count)}개 그룹, {filteredTabs.Sum(t => t.TagItems.Count)}개 태그",
                DiscoveredPatterns = originalResult.DiscoveredPatterns
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region 선택 가능 래퍼 클래스

        private class SelectableTab
        {
            public FLTabConfig Tab { get; }
            public bool IsSelected { get; set; } = true;
            public string DisplayName { get; set; } = ""; // 수정 가능한 탭 이름
            public List<SelectableGroup> Groups { get; } = new();

            public SelectableTab(FLTabConfig tab)
            {
                Tab = tab;
                DisplayName = tab.Name; // 초기값은 원본 탭 이름
                foreach (var group in tab.TagGroups)
                {
                    Groups.Add(new SelectableGroup(group));
                }
            }
        }

        private class SelectableGroup
        {
            public FLTagGroup Group { get; }
            public bool IsSelected { get; set; } = true;
            public string DisplayName { get; set; } = ""; // 수정 가능한 그룹명
            public List<SelectableTag> Tags { get; } = new();

            public SelectableGroup(FLTagGroup group)
            {
                Group = group;
                DisplayName = group.GroupName; // 초기값은 원본 그룹명
                int order = 1;
                foreach (var tag in group.Tags)
                {
                    Tags.Add(new SelectableTag(tag, order++));
                }
            }
        }

        private class SelectableTag
        {
            public FLTagItem Tag { get; }
            public int Order { get; }
            public bool IsSelected { get; set; } = true;

            public SelectableTag(FLTagItem tag, int order)
            {
                Tag = tag;
                Order = order;
            }
        }

        #endregion
    }
}
