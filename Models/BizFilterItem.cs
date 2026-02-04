using System.ComponentModel;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 비즈 필터 항목 (멀티 선택용)
    /// </summary>
    public class BizFilterItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        
        public string Name { get; set; } = "";
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
