using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    public class LogViewerManager
    {
        public ObservableCollection<LogEntry> LogEntries { get; private set; }

        public LogViewerManager()
        {
            LogEntries = new ObservableCollection<LogEntry>();
        }

        public void AddLog(string direction, string msgId, Dictionary<string, string> fields)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Direction = direction,
                MessageId = msgId,
                Fields = fields ?? new Dictionary<string, string>(),
                WorkType = fields?.GetValueOrDefault("WORK_TYPE", "") ?? "",
                ReturnCode = fields?.GetValueOrDefault("RETURN_CODE", "") ?? "",
                ErrorCode = fields?.GetValueOrDefault("ERROR_CODE", "") ?? ""
            };

            AddLogEntry(entry);
        }

        public void AddLogEntry(LogEntry entry)
        {
            LogEntries.Add(entry);
        }

        /// <summary>
        /// 일괄 추가 - UI 갱신 최소화
        /// </summary>
        public void AddLogEntries(IEnumerable<LogEntry> entries)
        {
            foreach (var entry in entries)
            {
                LogEntries.Add(entry);
            }
        }

        public void Clear()
        {
            LogEntries.Clear();
        }
    }
}
