using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class MainWindow : Window
    {
        private string currentLogDirectory = "";
        private bool isDefaultFolder = true;  // 기본폴더 모드 여부

        private ObservableCollection<AvailableDate> availableDates = new();

        public MainWindow()
        {
            InitializeComponent();

            cboAvailableDates.ItemsSource = availableDates;

            // 저장된 설정 로드
            LoadConfig();

            // 날짜 목록 갱신
            RefreshAvailableDates();

            // 자동 시작이 체크되어 있으면 바로 시작
            if (AppSettingsManager.Settings.AutoStart && availableDates.Count > 0)
            {
                Loaded += (s, e) => StartLogViewer();
            }
        }

        private void LoadConfig()
        {
            var settings = AppSettingsManager.Settings;
            
            // 마지막 사용 폴더가 있으면 해당 폴더로 시작
            if (!string.IsNullOrEmpty(settings.LastUsedFolder) && Directory.Exists(settings.LastUsedFolder))
            {
                currentLogDirectory = settings.LastUsedFolder;
                isDefaultFolder = settings.LastUsedFolder.Equals(settings.DefaultLogFolder, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                currentLogDirectory = settings.DefaultLogFolder;
                isDefaultFolder = true;
            }
            
            txtLogFolder.Text = currentLogDirectory;
            chkAutoStart.IsChecked = settings.AutoStart;
        }

        private void SaveConfig()
        {
            var settings = AppSettingsManager.Settings;
            settings.LastUsedFolder = currentLogDirectory;
            settings.AutoStart = chkAutoStart.IsChecked == true;
            AppSettingsManager.SaveCurrent();
        }

        /// <summary>
        /// 사용 가능한 날짜 목록 갱신
        /// </summary>
        private void RefreshAvailableDates()
        {
            availableDates.Clear();

            var dates = new List<AvailableDate>();

            if (isDefaultFolder)
            {
                // 기본폴더: 년/월 구조 검색
                dates = FindDatesInDefaultFolder(currentLogDirectory);
            }
            else
            {
                // 사용자 폴더: 해당 폴더 내 로그 파일 검색
                dates = FindDatesInCustomFolder(currentLogDirectory);
            }

            // 최신 날짜 순으로 정렬
            foreach (var date in dates.OrderByDescending(d => d.Date))
            {
                availableDates.Add(date);
            }

            // 첫 번째 (최신) 선택
            if (availableDates.Count > 0)
            {
                cboAvailableDates.SelectedIndex = 0;
                txtDateInfo.Text = $"총 {availableDates.Count}개의 로그 파일을 찾았습니다.";
            }
            else
            {
                txtDateInfo.Text = "로그 파일을 찾을 수 없습니다.";
            }
        }

        /// <summary>
        /// 기본폴더(년/월 구조)에서 로그 파일 검색
        /// </summary>
        private List<AvailableDate> FindDatesInDefaultFolder(string baseDir)
        {
            var result = new List<AvailableDate>();
            
            if (!Directory.Exists(baseDir))
                return result;

            // LGE GMES_EVENT_MMDDYYYY.log 패턴
            var filePattern = new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                // 년 폴더들 검색
                foreach (var yearDir in Directory.GetDirectories(baseDir))
                {
                    string yearName = Path.GetFileName(yearDir);
                    if (!int.TryParse(yearName, out int year) || year < 2000 || year > 2100)
                        continue;

                    // 월 폴더들 검색
                    foreach (var monthDir in Directory.GetDirectories(yearDir))
                    {
                        string monthName = Path.GetFileName(monthDir);
                        if (!int.TryParse(monthName, out int month) || month < 1 || month > 12)
                            continue;

                        // 로그 파일들 검색
                        foreach (var file in Directory.GetFiles(monthDir, "*.log"))
                        {
                            var match = filePattern.Match(Path.GetFileName(file));
                            if (match.Success)
                            {
                                int mm = int.Parse(match.Groups[1].Value);
                                int dd = int.Parse(match.Groups[2].Value);
                                int yyyy = int.Parse(match.Groups[3].Value);

                                try
                                {
                                    var date = new DateTime(yyyy, mm, dd);
                                    result.Add(new AvailableDate { Date = date, FilePath = file });
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴더 검색 오류: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 사용자 지정 폴더에서 로그 파일 검색
        /// </summary>
        private List<AvailableDate> FindDatesInCustomFolder(string folder)
        {
            var result = new List<AvailableDate>();

            if (!Directory.Exists(folder))
                return result;

            var filePattern = new Regex(@"LGE GMES_EVENT_(\d{2})(\d{2})(\d{4})\.log$", RegexOptions.IgnoreCase);

            try
            {
                // 현재 폴더와 하위 폴더에서 검색
                foreach (var file in Directory.GetFiles(folder, "*.log", SearchOption.AllDirectories))
                {
                    var match = filePattern.Match(Path.GetFileName(file));
                    if (match.Success)
                    {
                        int mm = int.Parse(match.Groups[1].Value);
                        int dd = int.Parse(match.Groups[2].Value);
                        int yyyy = int.Parse(match.Groups[3].Value);

                        try
                        {
                            var date = new DateTime(yyyy, mm, dd);
                            result.Add(new AvailableDate { Date = date, FilePath = file });
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폴더 검색 오류: {ex.Message}");
            }

            return result;
        }

        private void BtnSetDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            currentLogDirectory = AppSettingsManager.Settings.DefaultLogFolder;
            isDefaultFolder = true;
            txtLogFolder.Text = currentLogDirectory;
            RefreshAvailableDates();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            dialog.Title = "로그 폴더를 선택하세요";
            dialog.InitialDirectory = currentLogDirectory;

            if (dialog.ShowDialog() == true)
            {
                currentLogDirectory = dialog.FolderName;
                isDefaultFolder = false;  // 사용자 지정 폴더
                txtLogFolder.Text = currentLogDirectory;
                
                // 폴더 경로 저장
                SaveConfig();
                
                RefreshAvailableDates();
            }
        }


        private void BtnRefreshDates_Click(object sender, RoutedEventArgs e)
        {
            RefreshAvailableDates();
        }

        private void ChkAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            AppSettingsManager.Settings.AutoStart = chkAutoStart.IsChecked == true;
            AppSettingsManager.SaveCurrent();
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            StartLogViewer();
        }

        private void StartLogViewer()
        {
            // 선택된 날짜 확인
            var selectedItem = cboAvailableDates.SelectedItem as AvailableDate;
            if (selectedItem == null)
            {
                MessageBox.Show(
                    "로그 날짜를 선택해주세요.",
                    "알림",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            string logFilePath = selectedItem.FilePath;
            DateTime selectedDate = selectedItem.Date;

            System.Diagnostics.Debug.WriteLine($"📅 선택된 날짜: {selectedDate:yyyy-MM-dd}");
            System.Diagnostics.Debug.WriteLine($"📄 선택된 파일: {logFilePath}");

            // 로그 파일 확인
            if (!File.Exists(logFilePath))
            {
                MessageBox.Show(
                    $"로그 파일이 존재하지 않습니다:\n{logFilePath}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            // 옵션 확인
            LogLoadMode loadMode = LogLoadMode.NewOnly;
            int recentCount = 500;

            if (rbNewOnly.IsChecked == true)
            {
                loadMode = LogLoadMode.NewOnly;
            }
            else if (rbLoadRecent.IsChecked == true)
            {
                loadMode = LogLoadMode.Recent;
                if (!int.TryParse(txtRecentCount.Text, out recentCount) || recentCount <= 0)
                {
                    MessageBox.Show("개수는 1 이상의 숫자를 입력해주세요.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (rbLoadAll.IsChecked == true)
            {
                loadMode = LogLoadMode.All;
            }

            // 로그 뷰어 창 열기
            try
            {
                // 로그 파일에서 필드 목록 미리 추출
                var discoveredFields = LogFieldAnalyzer.ExtractFieldNames(logFilePath);
                LogFieldAnalyzer.AddDiscoveredFields(discoveredFields);
                System.Diagnostics.Debug.WriteLine($"🔍 발견된 필드: {discoveredFields.Count}개");

                var logViewerWindow = new LogViewerWindow(logFilePath, selectedDate, loadMode, recentCount);
                logViewerWindow.Show();

                this.Hide();

                // 로그 뷰어가 닫히면 설정 창 다시 표시
                logViewerWindow.Closed += (s, args) =>
                {
                    this.Show();
                    RefreshAvailableDates();  // 다시 열 때 날짜 목록 갱신
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"로그 뷰어 시작 중 오류 발생:\n{ex.Message}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }

    public enum LogLoadMode
    {
        NewOnly,    // 실행 시점 이후만
        Recent,     // 최근 N개
        All         // 전체
    }
}
