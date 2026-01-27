using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer
{
    public partial class TabSettingsWindow : Window
    {
        private ObservableCollection<TabConfig> tabs = new();
        private TabConfig? selectedTab;
        private bool isUpdating = false;

        /// <summary>
        /// 발견된 필드 목록 (콤보박스 바인딩용)
        /// </summary>
        public List<string> FieldList => LogFieldAnalyzer.DiscoveredFields;

        public TabSettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = ColumnSettingsManager.CurrentSettings;
            
            // 기존 탭 설정 복사
            tabs.Clear();
            if (settings.TabSettings?.Tabs != null)
            {
                foreach (var tab in settings.TabSettings.Tabs)
                {
                    var copy = new TabConfig
                    {
                        Name = tab.Name,
                        Order = tab.Order,
                        IsEnabled = tab.IsEnabled,
                        IsIntegrated = tab.IsIntegrated,
                        // 구버전 호환: Conditions가 있으면 첫 번째 그룹으로 변환
                        Conditions = tab.Conditions?.Select(c => new TabFilterCondition
                        {
                            FieldName = c.FieldName,
                            Value = c.Value,
                            ExactMatch = c.ExactMatch
                        }).ToList() ?? new List<TabFilterCondition>(),
                        // 새 버전: ConditionGroups 복사
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

                    // 구버전 호환: Conditions만 있고 ConditionGroups가 없으면 변환
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

            // 탭이 없으면 기본 통합 탭 추가
            if (tabs.Count == 0)
            {
                tabs.Add(new TabConfig
                {
                    Name = "All Logs",
                    Order = 0,
                    IsIntegrated = true,
                    IsEnabled = true
                });
            }

            listBoxTabs.ItemsSource = tabs;
            
            if (tabs.Count > 0)
            {
                listBoxTabs.SelectedIndex = 0;
            }
        }

        private void ListBoxTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedTab = listBoxTabs.SelectedItem as TabConfig;
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel()
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
            
            // 조건 그룹 ItemsSource 설정
            itemsConditionGroups.ItemsSource = null;
            itemsConditionGroups.ItemsSource = selectedTab.ConditionGroups;
            
            // 통합 탭이면 조건 비활성화
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
            
            // 통합 탭이면 조건 비활성화
            itemsConditionGroups.IsEnabled = !selectedTab.IsIntegrated;
            
            RefreshTabList();
        }

        private void TabEnabled_Changed(object sender, RoutedEventArgs e)
        {
            // 체크박스 변경 시 자동 저장됨 (바인딩)
        }

        private void Condition_Changed(object sender, RoutedEventArgs e)
        {
            // 조건 변경 시 탭 목록은 새로고침하지 않음 (포커스 유지)
            // 저장 시점에 반영됨
        }

        private void Condition_Changed(object sender, TextChangedEventArgs e)
        {
            // 텍스트 입력 중에는 새로고침하지 않음 (포커스 유지)
            // 바인딩이 UpdateSourceTrigger=PropertyChanged로 설정되어 자동 저장됨
        }

        private void Condition_Changed(object sender, SelectionChangedEventArgs e)
        {
            // ComboBox 선택 변경 시에도 새로고침하지 않음
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
                Name = $"New Tab {tabs.Count + 1}",
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

            // 최소 1개 탭은 유지
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

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var index = tabs.IndexOf(selectedTab);
            if (index > 0)
            {
                tabs.Move(index, index - 1);
                UpdateOrders();
                RefreshTabList();
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            var index = tabs.IndexOf(selectedTab);
            if (index < tabs.Count - 1)
            {
                tabs.Move(index, index + 1);
                UpdateOrders();
                RefreshTabList();
            }
        }

        private void UpdateOrders()
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].Order = i;
            }
        }

        private void BtnAddCondition_Click(object sender, RoutedEventArgs e)
        {
            // 구버전 호환용 - 이제 사용하지 않음
        }

        private void BtnRemoveCondition_Click(object sender, RoutedEventArgs e)
        {
            // 구버전 호환용 - 이제 사용하지 않음
        }

        // === 조건 그룹 관련 메서드들 ===

        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            selectedTab.ConditionGroups ??= new List<ConditionGroup>();
            selectedTab.ConditionGroups.Add(new ConditionGroup
            {
                Name = $"Group {selectedTab.ConditionGroups.Count + 1}",
                Conditions = new List<TabFilterCondition>
                {
                    new TabFilterCondition { FieldName = "MSGID", Value = "", ExactMatch = true }
                }
            });

            UpdateDetailPanel();
        }

        private void BtnRemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (sender is Button button && button.Tag is ConditionGroup group)
            {
                selectedTab.ConditionGroups?.Remove(group);
                UpdateDetailPanel();
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

                UpdateDetailPanel();
            }
        }

        private void BtnRemoveConditionInGroup_Click(object sender, RoutedEventArgs e)
        {
            if (selectedTab == null) return;

            if (sender is Button button && button.Tag is TabFilterCondition condition)
            {
                // 모든 그룹에서 해당 조건 찾아서 삭제
                foreach (var group in selectedTab.ConditionGroups ?? Enumerable.Empty<ConditionGroup>())
                {
                    if (group.Conditions?.Remove(condition) == true)
                    {
                        break;
                    }
                }
                UpdateDetailPanel();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // 순서 업데이트
            UpdateOrders();

            // 설정 저장
            var settings = ColumnSettingsManager.CurrentSettings;
            settings.TabSettings = new TabSettings
            {
                Tabs = tabs.ToList(),
                LastSelectedTabIndex = 0
            };

            ColumnSettingsManager.CurrentSettings = settings;

            DialogResult = true;
            Close();
        }
    }
}
