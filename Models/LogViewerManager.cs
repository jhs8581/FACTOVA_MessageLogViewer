using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    public class LogViewerManager
    {
        public ObservableCollection<LogEntry> LogEntries { get; private set; }
        private const int MAX_LOG_COUNT = 3000;

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
            if (LogEntries.Count >= MAX_LOG_COUNT)
            {
                LogEntries.RemoveAt(0);
            }



            LogEntries.Add(entry);
        }

        /// <summary>
        /// 일괄 추가 - UI 갱신 최소화
        /// </summary>
        public void AddLogEntries(IEnumerable<LogEntry> entries)
        {
            var list = entries as IList<LogEntry> ?? entries.ToList();
            
            // MAX_LOG_COUNT 초과 시 앞에서 제거
            int overflow = LogEntries.Count + list.Count - MAX_LOG_COUNT;
            if (overflow > 0)
            {
                for (int i = 0; i < Math.Min(overflow, LogEntries.Count); i++)
                {
                    LogEntries.RemoveAt(0);
                }
            }

            foreach (var entry in list)
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
