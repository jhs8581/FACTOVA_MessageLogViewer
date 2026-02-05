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
        /// 로그에서 파라미터 분석 후 그리드에 바로 추가
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

                // 모든 로그에서 파라미터 필드 및 샘플 값 추출
                var parameterSamples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var log in logEntries)
                {
                    if (log.Fields != null)
                    {
                        foreach (var kvp in log.Fields)
                        {
                            if (!parameterSamples.ContainsKey(kvp.Key))
                            {
                                parameterSamples[kvp.Key] = kvp.Value?.ToString() ?? "";
                            }
                            else if (string.IsNullOrEmpty(parameterSamples[kvp.Key]) && !string.IsNullOrEmpty(kvp.Value?.ToString()))
                            {
                                parameterSamples[kvp.Key] = kvp.Value?.ToString() ?? "";
                            }
                        }
                    }
                }

                if (!parameterSamples.Any())
                {
                    MessageBox.Show("파라미터를 찾을 수 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 이미 추가된 파라미터 목록
                var existingParams = new HashSet<string>(paramFields.Select(f => f.FieldName), StringComparer.OrdinalIgnoreCase);

                // 새로 발견된 파라미터들을 그리드에 바로 추가
                int addedCount = 0;
                int order = paramFields.Any() ? paramFields.Max(f => f.Order) + 1 : 100;

                foreach (var param in parameterSamples.OrderBy(p => p.Key))
                {
                    // 이미 추가된 파라미터는 건너뛰기
                    if (existingParams.Contains(param.Key))
                        continue;

                    if (order >= 900) order = 899;

                    paramFields.Add(new DataFieldConfig
                    {
                        Order = order++,
                        FieldName = param.Key,
                        DisplayName = param.Key,
                        ColumnWidth = 100,
                        IsEnabled = false,  // 기본값: Hidden
                        IsParameter = true,
                        SampleValue = string.IsNullOrEmpty(param.Value) ? "(no value)" : param.Value
                    });
                    addedCount++;
                }

                UpdateParamOrders();

                if (addedCount > 0)
                {
                    MessageBox.Show($"{addedCount}개의 새 파라미터가 추가되었습니다. (기본: Hidden)\n(총 {parameterSamples.Count}개 발견, {existingParams.Count}개는 이미 존재)", 
                        "분석 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"모든 파라미터가 이미 추가되어 있습니다.\n(총 {parameterSamples.Count}개 발견)", 
                        "분석 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"파라미터 분석 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveParam_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DataFieldConfig field)
            {
                paramFields.Remove(field);
                UpdateParamOrders();
            }
        }

        /// <summary>
        /// 파라미터 필드 위로 이동
        /// </summary>
        private void BtnParamMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgParamFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = paramFields.IndexOf(selectedItem);
            if (index > 0)
            {
                paramFields.Move(index, index - 1);
                UpdateParamOrders();
                dgParamFields.SelectedItem = selectedItem;
            }
        }

        /// <summary>
        /// 파라미터 필드 아래로 이동
        /// </summary>
        private void BtnParamMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = dgParamFields.SelectedItem as DataFieldConfig;
            if (selectedItem == null) return;

            var index = paramFields.IndexOf(selectedItem);
            if (index < paramFields.Count - 1)
            {
                paramFields.Move(index, index + 1);
                UpdateParamOrders();
                dgParamFields.SelectedItem = selectedItem;
            }
        }

        /// <summary>
        /// 파라미터 순서 번호 갱신
        /// </summary>
        private void UpdateParamOrders()
        {
            int order = 100;
            foreach (var field in paramFields)
            {
                field.Order = order++;
            }
            dgParamFields.Items.Refresh();
        }

        /// <summary>
        /// 체크한 항목 일괄 변경 (콤보박스 선택값으로)
        /// </summary>
        private void BtnBulkChange_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = paramFields.Where(f => f.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("선택된 항목이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedType = (cboBulkChange.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(selectedType))
            {
                MessageBox.Show("변경할 표시타입을 선택하세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var item in selectedItems)
            {
                item.DisplayTypeString = selectedType;
            }
            dgParamFields.Items.Refresh();
            MessageBox.Show($"{selectedItems.Count}개 항목이 '{selectedType}'으로 변경되었습니다.", "완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 컬럼 표시만 필터
        /// </summary>
        private void ChkColumnOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (dgParamFields == null) return;
            
            if (chkColumnOnly.IsChecked == true)
            {
                // Column 타입만 필터링하여 표시
                dgParamFields.ItemsSource = paramFields.Where(f => f.IsEnabled).ToList();
            }
            else
            {
                // 전체 표시
                dgParamFields.ItemsSource = paramFields;
            }
        }

        /// <summary>
        /// 선택된 항목 일괄 삭제
        /// </summary>
        private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = paramFields.Where(f => f.IsSelected).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("선택된 항목이 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"{selectedItems.Count}개 항목을 삭제하시겠습니까?", "확인",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    paramFields.Remove(item);
                }
                UpdateParamOrders();
            }
        }

        /// <summary>
        /// 값 매핑 편집
        /// </summary>
        private void BtnEditValueMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DataFieldConfig field)
            {
                var dialog = new InputDialog("값 매핑 설정", $"'{field.FieldName}'의 값 매핑을 입력하세요:\n예: 1=ON,0=OFF,Y=사용,N=미사용");
                dialog.Owner = this;
                
                // 기존 값 설정
                var textBox = FindTextBox(dialog);
                if (textBox != null)
                {
                    textBox.Text = field.ValueMapping;
                }

                if (dialog.ShowDialog() == true)
                {
                    field.ValueMapping = dialog.InputText.Trim();
                    dgParamFields.Items.Refresh();
                }
            }
        }

        private TextBox? FindTextBox(Window window)
        {
            if (window.Content is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBox tb)
                        return tb;
                }
            }
            return null;
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
