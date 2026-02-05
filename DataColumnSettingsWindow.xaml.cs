using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FACTOVA_MessageLogViewer
{
    public partial class DataColumnSettingsWindow : Window
    {
        private ObservableCollection<DataFieldConfig> basicFields = new();
        private ObservableCollection<DataFieldConfig> paramFields = new();
        private DataColumnSettings currentSettings = new();
        private string? initialPresetName;

        public bool SettingsApplied { get; private set; } = false;

        public DataColumnSettingsWindow() : this(null)
        {
        }

        public DataColumnSettingsWindow(string? selectedPresetName)
        {
            InitializeComponent();
            this.initialPresetName = selectedPresetName;

            dgBasicFields.ItemsSource = basicFields;
            dgParamFields.ItemsSource = paramFields;

            LoadPresetList();
            LoadSelectedPreset();
        }

        #region 프리셋 관리

        private void LoadPresetList()
        {
            cboPresets.Items.Clear();
            cboPresets.Items.Add("Default");

            // DATA 프리셋 목록 로드 (통합 프리셋에서)
            var presetNames = UnifiedPresetManager.GetPresetNames();
            foreach (var name in presetNames)
            {
                cboPresets.Items.Add(name);
            }

            // 전달받은 프리셋 이름으로 선택, 없으면 현재 프리셋 이름으로 선택
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
                if (preset?.DataSettings != null)
                {
                    currentSettings = preset.DataSettings;
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
            currentSettings = DataColumnSettings.CreateDefault();
            ApplySettingsToUI();
        }

        private void ApplySettingsToUI()
        {
            basicFields.Clear();
            paramFields.Clear();

            foreach (var field in currentSettings.ColumnFields)
            {
                if (field.IsParameter)
                    paramFields.Add(field);
                else
                    basicFields.Add(field);
            }

            // 기본 필드가 없으면 추가
            if (!basicFields.Any())
            {
                basicFields.Add(new DataFieldConfig { Order = 1, FieldName = "RowNumber", DisplayName = "#", ColumnWidth = 50, IsEnabled = true });
                basicFields.Add(new DataFieldConfig { Order = 2, FieldName = "TimeString", DisplayName = "시간", ColumnWidth = 90, IsEnabled = true });
                basicFields.Add(new DataFieldConfig { Order = 10, FieldName = "BizName", DisplayName = "비즈명", ColumnWidth = 280, IsEnabled = true });
                // 파라미터 컬럼은 Order 100~899 사이에 배치 (비즈명 뒤, 실행시간 앞)
                basicFields.Add(new DataFieldConfig { Order = 900, FieldName = "ExecTime", DisplayName = "실행시간", ColumnWidth = 100, IsEnabled = true });
                basicFields.Add(new DataFieldConfig { Order = 901, FieldName = "TxnId", DisplayName = "TXN_ID", ColumnWidth = 180, IsEnabled = true });
                basicFields.Add(new DataFieldConfig { Order = 902, FieldName = "ClientId", DisplayName = "CLIENT_ID", ColumnWidth = 100, IsEnabled = false });
                basicFields.Add(new DataFieldConfig { Order = 903, FieldName = "ClientIp", DisplayName = "CLIENT_IP", ColumnWidth = 100, IsEnabled = false });
                basicFields.Add(new DataFieldConfig { Order = 999, FieldName = "Summary", DisplayName = "파라미터", ColumnWidth = 0, IsEnabled = true });
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
                if (preset?.DataSettings != null)
                {
                    currentSettings = preset.DataSettings;
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

                // 중복 체크
                if (cboPresets.Items.Contains(newName))
                {
                    MessageBox.Show("이미 존재하는 프리셋 이름입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 현재 설정으로 새 프리셋 생성
                currentSettings.Name = newName;
                SaveCurrentSettingsToPreset(newName);

                LoadPresetList();

                // 새 프리셋 선택
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

        #region 파라미터 필드 관리

        /// <summary>
        /// 로그에서 파라미터 분석
        /// </summary>
        private void BtnAnalyzeParams_Click(object sender, RoutedEventArgs e)
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

                // DataLogViewerControl 찾기
                var dataControl = mainWindow.FindName("dataLogViewer") as DataLogViewerControl;
                if (dataControl == null)
                {
                    MessageBox.Show("DATA 로그 컨트롤을 찾을 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Reflection을 사용하여 logEntries에 접근
                var logEntriesField = typeof(DataLogViewerControl).GetField("logEntries", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (logEntriesField == null)
                {
                    MessageBox.Show("로그 데이터에 접근할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var logEntries = logEntriesField.GetValue(dataControl) as ObservableCollection<DataLogEntry>;
                if (logEntries == null || !logEntries.Any())
                {
                    MessageBox.Show("로드된 DATA 로그가 없습니다.\n먼저 로그를 로드해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 모든 로그에서 파라미터 필드 추출
                var parameterSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var log in logEntries)
                {
                    if (log.Fields != null)
                    {
                        foreach (var key in log.Fields.Keys)
                        {
                            parameterSet.Add(key);
                        }
                    }
                }

                if (!parameterSet.Any())
                {
                    MessageBox.Show("파라미터를 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 콤보박스에 추가
                cboAvailableParams.Items.Clear();
                foreach (var param in parameterSet.OrderBy(p => p))
                {
                    cboAvailableParams.Items.Add(param);
                }

                if (cboAvailableParams.Items.Count > 0)
                {
                    cboAvailableParams.SelectedIndex = 0;
                }

                MessageBox.Show($"{parameterSet.Count}개의 파라미터를 발견했습니다.", "분석 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파라미터 분석 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 콤보박스에서 선택한 파라미터 추가
        /// </summary>
        private void BtnAddParamFromCombo_Click(object sender, RoutedEventArgs e)
        {
            var fieldName = cboAvailableParams.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(fieldName))
            {
                MessageBox.Show("파라미터를 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var displayName = txtParamDisplayFromCombo.Text.Trim();
            if (string.IsNullOrEmpty(displayName))
                displayName = fieldName;

            // 중복 체크
            if (paramFields.Any(f => f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("이미 추가된 파라미터입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 기본 Order: 비즈명(10) 뒤, 실행시간(900) 앞 = 100~899 범위
            int order = 100;
            if (paramFields.Any())
            {
                order = paramFields.Max(f => f.Order) + 1;
                if (order >= 900) order = 899; // 실행시간 앞까지만
            }

            paramFields.Add(new DataFieldConfig
            {
                Order = order,
                FieldName = fieldName,
                DisplayName = displayName,
                ColumnWidth = 100,
                IsEnabled = true,
                IsParameter = true
            });

            txtParamDisplayFromCombo.Text = "";

            // 다음 항목 선택
            if (cboAvailableParams.SelectedIndex < cboAvailableParams.Items.Count - 1)
            {
                cboAvailableParams.SelectedIndex++;
            }
        }

        private void BtnRemoveParam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DataFieldConfig field)
            {
                paramFields.Remove(field);
            }
        }

        #endregion

        #region 저장/취소

        private void CollectSettingsFromUI()
        {
            currentSettings.ColumnFields.Clear();

            foreach (var field in basicFields)
            {
                field.IsParameter = false;
                currentSettings.ColumnFields.Add(field);
            }

            foreach (var field in paramFields)
            {
                field.IsParameter = true;
                currentSettings.ColumnFields.Add(field);
            }
        }

        private void SaveCurrentSettingsToPreset(string presetName)
        {
            CollectSettingsFromUI();

            // 통합 프리셋에 저장
            var preset = UnifiedPresetManager.LoadPreset(presetName) ?? new UnifiedPreset { Name = presetName };
            preset.DataSettings = currentSettings;
            preset.DataSettings.Name = presetName;
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

            var enabledFields = currentSettings.ColumnFields.Where(f => f.IsEnabled).ToList();
            var preview = string.Join("\n", enabledFields.Select(f =>
                $"[{(f.IsParameter ? "P" : "B")}] {f.DisplayName} ({f.FieldName}) - {(f.ColumnWidth == 0 ? "*" : f.ColumnWidth.ToString())}px" +
                (string.IsNullOrEmpty(f.ValueMapping) ? "" : $" 변환:{f.ValueMapping}")));

            MessageBox.Show($"활성화된 컬럼 ({enabledFields.Count}개):\n\n{preview}",
                "미리보기", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var presetName = cboPresets.SelectedItem?.ToString() ?? "Default";

            if (presetName == "Default")
            {
                // Default는 통합 프리셋에 저장하지 않고 현재 세션에만 적용
                CollectSettingsFromUI();

                // 현재 프리셋에 반영
                if (UnifiedPresetManager.CurrentPreset.DataSettings == null)
                    UnifiedPresetManager.CurrentPreset.DataSettings = new DataColumnSettings();

                UnifiedPresetManager.CurrentPreset.DataSettings = currentSettings;
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
                // Default는 통합 프리셋에 저장하지 않고 현재 세션에만 적용
                CollectSettingsFromUI();

                // 현재 프리셋에 반영
                if (UnifiedPresetManager.CurrentPreset.DataSettings == null)
                    UnifiedPresetManager.CurrentPreset.DataSettings = new DataColumnSettings();

                UnifiedPresetManager.CurrentPreset.DataSettings = currentSettings;
            }
            else
            {
                SaveCurrentSettingsToPreset(presetName);
            }

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

        #region 순서 이동

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgBasicFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = basicFields.IndexOf(selectedItem);
            if (index > 0)
            {
                // Order 값 교환
                var prevItem = basicFields[index - 1];
                var tempOrder = selectedItem.Order;
                selectedItem.Order = prevItem.Order;
                prevItem.Order = tempOrder;

                // 컬렉션에서 위치 변경
                basicFields.Move(index, index - 1);
                dgBasicFields.Items.Refresh();
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgBasicFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = basicFields.IndexOf(selectedItem);
            if (index < basicFields.Count - 1)
            {
                // Order 값 교환
                var nextItem = basicFields[index + 1];
                var tempOrder = selectedItem.Order;
                selectedItem.Order = nextItem.Order;
                nextItem.Order = tempOrder;

                // 컬렉션에서 위치 변경
                basicFields.Move(index, index + 1);
                dgBasicFields.Items.Refresh();
            }
        }

        #endregion
    }

    /// <summary>
    /// 간단한 입력 다이얼로그
    /// </summary>
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = "";

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 350;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox { Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button { Content = "확인", Width = 70, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
            okButton.Click += (s, e) => { InputText = textBox.Text; DialogResult = true; };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button { Content = "취소", Width = 70, Height = 28 };
            cancelButton.Click += (s, e) => { DialogResult = false; };
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(buttonPanel);
            Content = grid;
        }
    }
}
