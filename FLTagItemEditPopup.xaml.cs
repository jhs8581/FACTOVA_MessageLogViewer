using FACTOVA_MessageLogViewer.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FACTOVA_MessageLogViewer
{
    public partial class FLTagItemEditPopup : Window
    {
        public FLTagItem TagItem { get; set; }

        public FLTagItemEditPopup(FLTagItem tagItem, IEnumerable<FLLogEntry>? logEntries = null)
        {
            InitializeComponent();
            TagItem = tagItem;
            DataContext = TagItem;

            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"🔍 값 필터 팝업 열림");
            System.Diagnostics.Debug.WriteLine($"   태그명: {tagItem.TagName}");
            System.Diagnostics.Debug.WriteLine($"   현재 값 필터: '{tagItem.ValueFilter}'");
            System.Diagnostics.Debug.WriteLine($"========================================");

            var distinctValues = new HashSet<string>();

            // 로그에서 해당 태그의 실제 값들 추출
            if (logEntries != null)
            {
                var allEntries = logEntries.ToList();
                System.Diagnostics.Debug.WriteLine($"📊 전체 로그 엔트리: {allEntries.Count}개");

                var tagEntries = allEntries
                    .Where(e => e.TagName == tagItem.TagName)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"🎯 해당 태그 엔트리: {tagEntries.Count}개");

                int structureCount = 0;
                int emptyCount = 0;
                int validCount = 0;

                foreach (var entry in tagEntries)
                {
                    if (entry.IsStructure)
                    {
                        structureCount++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        emptyCount++;
                        continue;
                    }

                    distinctValues.Add(entry.Value);
                    validCount++;
                }

                System.Diagnostics.Debug.WriteLine($"   - Structure 타입: {structureCount}개 (제외)");
                System.Diagnostics.Debug.WriteLine($"   - 빈 값: {emptyCount}개 (제외)");
                System.Diagnostics.Debug.WriteLine($"   - 유효한 값: {validCount}개");
                System.Diagnostics.Debug.WriteLine($"   - 고유 값: {distinctValues.Count}개");

                if (distinctValues.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"📝 추출된 고유 값 목록:");
                    foreach (var value in distinctValues.OrderBy(v => v))
                    {
                        System.Diagnostics.Debug.WriteLine($"      '{value}'");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 로그 엔트리가 null입니다!");
            }

            // ComboBox에 추가 (정렬)
            int addedCount = 0;
            foreach (var value in distinctValues.OrderBy(v => v))
            {
                cboValueFilter.Items.Add(value);
                addedCount++;
            }

            System.Diagnostics.Debug.WriteLine($"✅ ComboBox에 {addedCount}개 값 추가 완료");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"");

            // 현재 값 설정
            cboValueFilter.Text = tagItem.ValueFilter ?? "";
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            // ComboBox의 텍스트를 TagItem에 반영
            TagItem.ValueFilter = cboValueFilter.Text?.Trim() ?? "";
            System.Diagnostics.Debug.WriteLine($"✅ 값 필터 설정: '{TagItem.ValueFilter}'");
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"❌ 취소됨");
            DialogResult = false;
            Close();
        }
    }
}




