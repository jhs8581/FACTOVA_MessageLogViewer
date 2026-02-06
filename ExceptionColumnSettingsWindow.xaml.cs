using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer
{
    public partial class ExceptionColumnSettingsWindow : Window
    {
        private ObservableCollection<DataFieldConfig> fields = new();
        private DataColumnSettings currentSettings = new();
        private string? initialPresetName;

        public bool SettingsApplied { get; private set; } = false;

        public ExceptionColumnSettingsWindow() : this(null)
        {
        }

        public ExceptionColumnSettingsWindow(string? selectedPresetName)
        {
            InitializeComponent();
            this.initialPresetName = selectedPresetName;
            
            dgFields.ItemsSource = fields;
            
            LoadPresetList();
            LoadSelectedPreset();
        }

        #region 프리셋 관리

        private void LoadPresetList()
        {
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");

            var presetNames = UnifiedPresetManager.GetPresetNames();
            foreach (var name in presetNames)
            {
                cboPresets.Items.Add(name);
            }

            var targetPreset = initialPresetName ?? UnifiedPresetManager.CurrentPreset?.Name;
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
        }

        private void LoadSelectedPreset()
        {
            var presetName = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(presetName) || presetName == "Default")
            {
                LoadDefaultSettings();
            }
            else
            {
                var preset = UnifiedPresetManager.LoadPreset(presetName);
                if (preset?.ExceptionSettings != null)
                {
                    currentSettings = preset.ExceptionSettings;
                    ApplySettingsToUI();
                }
                else
                {
                    LoadDefaultSettings();
                }
            }
        }

        private void LoadDefaultSettings()
        {
            currentSettings = DataColumnSettings.CreateExceptionDefault();
            ApplySettingsToUI();
        }

        private void ApplySettingsToUI()
        {
            fields.Clear();

            foreach (var field in currentSettings.ColumnFields.OrderBy(f => f.Order))
            {
                fields.Add(field);
            }

            // 기본 필드가 없으면 추가
            if (!fields.Any())
            {
                fields.Add(new DataFieldConfig { Order = 1, FieldName = "RowNumber", DisplayName = "#", ColumnWidth = 50, IsEnabled = true });
                fields.Add(new DataFieldConfig { Order = 2, FieldName = "TimeString", DisplayName = "시간", ColumnWidth = 90, IsEnabled = true });
                fields.Add(new DataFieldConfig { Order = 10, FieldName = "ExceptionType", DisplayName = "예외 타입", ColumnWidth = 200, IsEnabled = true });
                fields.Add(new DataFieldConfig { Order = 20, FieldName = "Message", DisplayName = "메시지", ColumnWidth = 400, IsEnabled = true });
                fields.Add(new DataFieldConfig { Order = 30, FieldName = "Source", DisplayName = "소스", ColumnWidth = 150, IsEnabled = true });
                fields.Add(new DataFieldConfig { Order = 999, FieldName = "Summary", DisplayName = "상세", ColumnWidth = 0, IsEnabled = true });
            }
        }

        private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(presetName)) return;

            if (presetName == "Default")
            {
                LoadDefaultSettings();
            }
            else
            {
                var preset = UnifiedPresetManager.LoadPreset(presetName);
                if (preset?.ExceptionSettings != null)
                {
                    currentSettings = preset.ExceptionSettings;
                    ApplySettingsToUI();
                }
                else
                {
                    LoadDefaultSettings();
                }
            }
        }

        private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog("새 프리셋", "프리셋 이름을 입력하세요:");
            inputDialog.Owner = this;
            
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
            {
                var newName = inputDialog.InputText.Trim();
                
                if (cboPresets.Items.Contains(newName))
                {
                    MessageBox.Show("이미 존재하는 프리셋 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                currentSettings.Name = newName;
                SaveCurrentSettingsToPreset(newName);
                
                LoadPresetList();
                
                for (int i = 0; i < cboPresets.Items.Count; i++)
                {
                    if (cboPresets.Items[i]?.ToString() == newName)
                    {
                        cboPresets.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(presetName) || presetName == "Default")
            {
                MessageBox.Show("Default 프리셋은 삭제할 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"'{presetName}' 프리셋을 삭제하시겠습니까?", "확인", 
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                UnifiedPresetManager.DeletePreset(presetName);
                LoadPresetList();
            }
        }

        #endregion

        #region 순서 이동

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = fields.IndexOf(selectedItem);
            if (index > 0)
            {
                var prevItem = fields[index - 1];
                var tempOrder = selectedItem.Order;
                selectedItem.Order = prevItem.Order;
                prevItem.Order = tempOrder;

                fields.Move(index, index - 1);
                dgFields.Items.Refresh();
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = fields.IndexOf(selectedItem);
            if (index < fields.Count - 1)
            {
                var nextItem = fields[index + 1];
                var tempOrder = selectedItem.Order;
                selectedItem.Order = nextItem.Order;
                nextItem.Order = tempOrder;

                fields.Move(index, index + 1);
                dgFields.Items.Refresh();
            }
        }

        #endregion

        #region 저장/취소

        private void CollectSettingsFromUI()
        {
            currentSettings.ColumnFields.Clear();
            
            foreach (var field in fields)
            {
                currentSettings.ColumnFields.Add(field);
            }
        }

        private void SaveCurrentSettingsToPreset(string presetName)
        {
            CollectSettingsFromUI();

            var preset = UnifiedPresetManager.LoadPreset(presetName) ?? new UnifiedPreset { Name = presetName };
            preset.ExceptionSettings = currentSettings;
            preset.ExceptionSettings.Name = presetName;
            UnifiedPresetManager.SavePreset(preset);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("기본값으로 초기화하시겠습니까?", "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                LoadDefaultSettings();
            }
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            CollectSettingsFromUI();

            var enabledFields = currentSettings.ColumnFields.Where(f => f.IsEnabled).OrderBy(f => f.Order).ToList();
            var preview = string.Join("\n", enabledFields.Select(f => 
                $"{f.DisplayName} ({f.FieldName}) - {(f.ColumnWidth == 0 ? "*" : f.ColumnWidth.ToString())}px"));

            MessageBox.Show($"활성화된 컬럼 ({enabledFields.Count}개):\n\n{preview}", 
                "미리보기", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString() ?? "Default";
            
            if (presetName == "Default")
            {
                CollectSettingsFromUI();
                
                if (UnifiedPresetManager.CurrentPreset.ExceptionSettings == null)
                    UnifiedPresetManager.CurrentPreset.ExceptionSettings = new DataColumnSettings();
                
                UnifiedPresetManager.CurrentPreset.ExceptionSettings = currentSettings;
            }
            else
            {
                SaveCurrentSettingsToPreset(presetName);
            }

            SettingsApplied = true;
            DialogResult = true;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString() ?? "Default";
            
            if (presetName == "Default")
            {
                CollectSettingsFromUI();
                
                if (UnifiedPresetManager.CurrentPreset.ExceptionSettings == null)
                    UnifiedPresetManager.CurrentPreset.ExceptionSettings = new DataColumnSettings();
                
                UnifiedPresetManager.CurrentPreset.ExceptionSettings = currentSettings;
            }
            else
            {
                SaveCurrentSettingsToPreset(presetName);
            }
            
            // 현재 프리셋 이름 저장
            AppSettingsManager.Settings.CurrentPresetName = presetName;
            AppSettingsManager.SaveCurrent();

            SettingsApplied = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOpenPresetFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var presetFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");

                // 폴더가 없으면 생성
                if (!System.IO.Directory.Exists(presetFolder))
                {
                    System.IO.Directory.CreateDirectory(presetFolder);
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

        private void TxtPresetName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            btnClearPresetName.Visibility = string.IsNullOrEmpty(txtPresetName.Text) 
                ? System.Windows.Visibility.Collapsed 
                : System.Windows.Visibility.Visible;
        }

        private void BtnClearPresetName_Click(object sender, RoutedEventArgs e)
        {
            txtPresetName.Clear();
        }

        #endregion
    }
}
