using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 로그 파일 분석기 - 사용 가능한 필드 추출
    /// </summary>
    public static class LogFieldAnalyzer
    {
        /// <summary>
        /// 발견된 필드명 목록 (전역 캐시) - 대소문자 무시
        /// </summary>
        private static HashSet<string> _discoveredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        /// <summary>
        /// 발견된 필드 목록 반환
        /// </summary>
        public static List<string> DiscoveredFields
        {
            get
            {
                lock (_lock)
                {
                    // 기본 필드 + 발견된 필드 (대소문자 무시)
                    var allFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    // 기본 필드 추가
                    allFields.Add("MSGID");
                    allFields.Add("WORK_TYPE");
                    allFields.Add("RETURN_CODE");
                    allFields.Add("ERROR_CODE");
                    allFields.Add("DIRECTION");
                    
                    // 발견된 필드 추가
                    foreach (var field in _discoveredFields)
                    {
                        allFields.Add(field);
                    }
                    
                    return allFields.OrderBy(f => f).ToList();
                }
            }
        }

        /// <summary>
        /// 필드명 추가
        /// </summary>
        public static void AddDiscoveredField(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return;
            
            lock (_lock)
            {
                _discoveredFields.Add(fieldName);
            }
        }

        /// <summary>
        /// 여러 필드명 추가
        /// </summary>
        public static void AddDiscoveredFields(IEnumerable<string> fieldNames)
        {
            if (fieldNames == null) return;
            
            lock (_lock)
            {
                foreach (var name in fieldNames.Where(n => !string.IsNullOrWhiteSpace(n)))
                {
                    _discoveredFields.Add(name);
                }
            }
        }

        /// <summary>
        /// 로그 파일에서 모든 필드명 추출 (샘플링) - 제외 MSGID 적용
        /// </summary>
        public static List<string> ExtractFieldNames(string logFilePath, int sampleSize = 100)
        {
            var fieldNames = new HashSet<string>();

            if (!File.Exists(logFilePath))
                return fieldNames.ToList();

            try
            {
                // 제외할 MSGID 목록 가져오기
                var excludedMsgIds = ColumnSettingsManager.CurrentSettings.ExcludedMsgIdSet;

                // 파일 읽기 (최대 1MB만)
                string content;
                using (var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var buffer = new char[1024 * 1024]; // 1MB
                    int read = reader.Read(buffer, 0, buffer.Length);
                    content = new string(buffer, 0, read);
                }

                // 로그 엔트리 단위로 분리
                var entryPattern = new Regex(@"^\[(\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]\[([A-Z]+)\]", 
                    RegexOptions.Compiled | RegexOptions.Multiline);
                var entryMatches = entryPattern.Matches(content);

                for (int i = 0; i < entryMatches.Count; i++)
                {
                    int startIndex = entryMatches[i].Index;
                    int endIndex = (i + 1 < entryMatches.Count) ? entryMatches[i + 1].Index : content.Length;
                    string entryText = content.Substring(startIndex, endIndex - startIndex);

                    // MSGID 추출하여 제외 목록 체크
                    var msgIdMatch = Regex.Match(entryText, @"<MSGID=([^>]*)>");
                    if (msgIdMatch.Success)
                    {
                        string msgId = msgIdMatch.Groups[1].Value;
                        if (excludedMsgIds.Contains(msgId))
                        {
                            continue; // 제외 MSGID면 스킵
                        }
                    }

                    // NAME=xxx 패턴 추출
                    var namePattern = new Regex(@"<NAME=([^>]+)>", RegexOptions.Compiled);
                    var matches = namePattern.Matches(entryText);

                    foreach (Match match in matches)
                    {
                        var fieldName = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(fieldName))
                        {
                            fieldNames.Add(fieldName);
                        }
                    }
                }

                // 추가로 ELEMENT 섹션의 필드들도 추가 (기본 필드)
                fieldNames.Add("PROCID");
                fieldNames.Add("MSGID");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"필드 분석 실패: {ex.Message}");
            }

            return fieldNames.OrderBy(f => f).ToList();
        }

        /// <summary>
        /// 필드별 샘플 값 추출 - 제외 MSGID 적용
        /// </summary>
        public static Dictionary<string, List<string>> ExtractFieldSamples(string logFilePath, int maxSamples = 5)
        {
            var samples = new Dictionary<string, List<string>>();

            if (!File.Exists(logFilePath))
                return samples;

            try
            {
                // 제외할 MSGID 목록 가져오기
                var excludedMsgIds = ColumnSettingsManager.CurrentSettings.ExcludedMsgIdSet;

                string content;
                using (var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var buffer = new char[1024 * 1024]; // 1MB
                    int read = reader.Read(buffer, 0, buffer.Length);
                    content = new string(buffer, 0, read);
                }

                // 로그 엔트리 단위로 분리
                var entryPattern = new Regex(@"^\[(\d{2}-\d{2}-\d{4}\s+\d{2}:\d{2}:\d{2}(?:\.\d{3})?)\]\[([A-Z]+)\]", 
                    RegexOptions.Compiled | RegexOptions.Multiline);
                var entryMatches = entryPattern.Matches(content);

                for (int i = 0; i < entryMatches.Count; i++)
                {
                    int startIndex = entryMatches[i].Index;
                    int endIndex = (i + 1 < entryMatches.Count) ? entryMatches[i + 1].Index : content.Length;
                    string entryText = content.Substring(startIndex, endIndex - startIndex);

                    // MSGID 추출하여 제외 목록 체크
                    var msgIdMatch = Regex.Match(entryText, @"<MSGID=([^>]*)>");
                    if (msgIdMatch.Success)
                    {
                        string msgId = msgIdMatch.Groups[1].Value;
                        if (excludedMsgIds.Contains(msgId))
                        {
                            continue; // 제외 MSGID면 스킵
                        }
                    }

                    // NAME/VALUE 쌍 추출
                    var pattern = new Regex(@"<NAME=([^>]+)>\s*<VALUE=([^>]*)>", RegexOptions.Compiled);
                    var matches = pattern.Matches(entryText);

                    foreach (Match match in matches)
                    {
                        var fieldName = match.Groups[1].Value.Trim();
                        var value = match.Groups[2].Value.Trim();

                        if (string.IsNullOrEmpty(fieldName))
                            continue;

                        if (!samples.ContainsKey(fieldName))
                            samples[fieldName] = new List<string>();

                        // 중복 아니고 maxSamples 이하면 추가
                        if (!samples[fieldName].Contains(value) && samples[fieldName].Count < maxSamples)
                        {
                            if (!string.IsNullOrEmpty(value))
                                samples[fieldName].Add(value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"샘플 추출 실패: {ex.Message}");
            }

            return samples;
        }

        /// <summary>
        /// 필드 분석 결과
        /// </summary>
        public static List<FieldAnalysisResult> AnalyzeFields(string logFilePath)
        {
            var results = new List<FieldAnalysisResult>();
            var fieldNames = ExtractFieldNames(logFilePath);
            var samples = ExtractFieldSamples(logFilePath);

            foreach (var fieldName in fieldNames)
            {
                var result = new FieldAnalysisResult
                {
                    FieldName = fieldName,
                    SampleValues = samples.ContainsKey(fieldName) ? samples[fieldName] : new List<string>()
                };

                // 기존 설정이 있으면 적용
                var existingConfig = ColumnSettingsManager.CurrentSettings.Fields
                    .FirstOrDefault(f => f.FieldName == fieldName);
                
                if (existingConfig != null)
                {
                    result.DisplayType = existingConfig.DisplayType;
                    result.DisplayName = existingConfig.DisplayName;
                    result.ColumnWidth = existingConfig.ColumnWidth;
                }

                results.Add(result);
            }

            return results;
        }
    }

    /// <summary>
    /// 필드 분석 결과
    /// </summary>
    public class FieldAnalysisResult
    {
        public string FieldName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> SampleValues { get; set; } = new();
        public FieldDisplayType DisplayType { get; set; } = FieldDisplayType.Summary;
        public int ColumnWidth { get; set; } = 100;

        public string SamplePreview => SampleValues.Count > 0 
            ? string.Join(", ", SampleValues.Take(3)) 
            : "(값 없음)";
    }
}
