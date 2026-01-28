using System;

namespace FACTOVA_MessageLogViewer
{
    public class AvailableDate
    {
        public DateTime Date { get; set; }
        public string DisplayText => Date.ToString("yyyy-MM-dd (ddd)");
        public string FilePath { get; set; } = "";
    }
}
