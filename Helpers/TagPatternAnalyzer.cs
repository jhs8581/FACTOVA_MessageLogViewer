using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FACTOVA_MessageLogViewer.Helpers
{
    /// <summary>
    /// F/L 태그 패턴 자동 분석기
    /// 
    /// 태그명 구조:
    /// [IO]_[TYPE]_[KIND]_[BUSINESS]_[ACTION]_[SUFFIX]
    /// 
    /// 예시: O_LB_EVENT_LOT_INFO_SEND_01
    ///       │  │    │     │        │    │
    ///       │  │    │     │        │    └── 접미사 (장비/스테이션 번호)
    ///       │  │    │     │        └── 액션 (SEND, REQUEST, CONFIRM, REPORT)
    ///       │  │    │     └── 업무영역 (LOT_INFO, REASON_CODE 등)
    ///       │  │    └── 종류 (EVENT → LB와 함께, DATA → LW와 함께)
    ///       │  └── 타입 (LB=Boolean, LW=Word, LD=Double Word)
    ///       └── 방향 (I=Input/IN, O=Output/OUT)
    /// </summary>
    public static class TagPatternAnalyzer
    {
        // 접미사 패턴: _XX (숫자 2자리)
        private static readonly Regex SuffixPattern = new Regex(@"_(\d{2})$", RegexOptions.Compiled);
        
        // 전체 태그명 패턴 분석
        // [IO]_[TYPE]_[KIND]_[BUSINESS...]_[ACTION]_[SUFFIX]
        private static readonly Regex FullTagPattern = new Regex(
            @"^([IO])_([A-Z]{2})_([A-Z]+)_(.+?)_(REQUEST|SEND|CONFIRM|REPORT)(?:_CONFIRM)?_(\d{2})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 간단한 태그명 패턴 (ACTION 없는 경우)
        private static readonly Regex SimpleTagPattern = new Regex(
            @"^([IO])_([A-Z]{2})_([A-Z]+)_(.+)_(\d{2})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 알려진 액션 키워드
        private static readonly string[] ActionKeywords = new[]
        {
            "REQUEST", "SEND", "CONFIRM", "REPORT"
        };

        // 알려진 업무영역 키워드
        private static readonly string[] BusinessKeywords = new[]
        {
            "LOT_INFO", "LOT_CONFIRM_DATA", "LOT_PROCESSING_COMPLETED_REPORT",
            "MATERIAL_INSPECTION_REPORT", "REASON_CODE", "RECIPE",
            "ALARM", "STATUS", "MODE", "CONTROL"
        };

        /// <summary>
        /// 분석 결과 클래스
        /// </summary>
        public class AnalysisResult
        {
            public List<FLTabConfig> Tabs { get; set; } = new();
            public List<FLTagConfig> TagConfigs { get; set; } = new();
            public string Summary { get; set; } = "";
            public List<string> DiscoveredPatterns { get; set; } = new();
        }

        /// <summary>
        /// 태그 분석 정보
        /// </summary>
        public class TagAnalysisInfo
        {
            public string TagName { get; set; } = "";
            public string Suffix { get; set; } = "";              // _01, _02 등
            public string IoDirection { get; set; } = "";         // I 또는 O
            public string IoDirectionDisplay { get; set; } = "";  // IN 또는 OUT
            public string DataType { get; set; } = "";            // LB, LW, LD
            public string DataTypeDisplay { get; set; } = "";     // Boolean, Word, DWord
            public string Kind { get; set; } = "";                // EVENT, DATA
            public string Business { get; set; } = "";            // LOT_INFO, REASON_CODE 등
            public string Action { get; set; } = "";              // REQUEST, SEND, CONFIRM, REPORT
            public bool IsConfirm { get; set; } = false;          // CONFIRM 응답인지
            public string Value { get; set; } = "";               // ON, OFF 등
            public string GroupKey { get; set; } = "";            // 그룹핑 키 (BUSINESS_ACTION)
        }

        /// <summary>
        /// 로그 엔트리에서 패턴 분석 수행
        /// </summary>
        public static AnalysisResult AnalyzeEntries(IEnumerable<FLLogEntry> entries)
        {
            var result = new AnalysisResult();
            var allTags = new List<TagAnalysisInfo>();

            // 1단계: 각 태그 분석
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.TagName)) continue;

                var info = AnalyzeTagName(entry.TagName, entry.Value);
                
                // 중복 제거 (같은 태그+값 조합)
                if (!allTags.Any(t => t.TagName == info.TagName && t.Value == info.Value))
                {
                    allTags.Add(info);
                }
            }

            if (allTags.Count == 0)
            {
                result.Summary = "분석할 태그가 없습니다.";
                return result;
            }

            // 발견된 패턴 기록
            var suffixes = allTags.Select(t => t.Suffix).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();
            var businesses = allTags.Select(t => t.Business).Where(b => !string.IsNullOrEmpty(b)).Distinct().OrderBy(b => b).ToList();
            var actions = allTags.Select(t => t.Action).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();

            result.DiscoveredPatterns.Add($"접미사: {string.Join(", ", suffixes)}");
            result.DiscoveredPatterns.Add($"업무영역: {string.Join(", ", businesses)}");
            result.DiscoveredPatterns.Add($"액션: {string.Join(", ", actions)}");

            // 2단계: 접미사별로 탭 생성
            var suffixGroups = allTags
                .GroupBy(t => string.IsNullOrEmpty(t.Suffix) ? "기타" : t.Suffix)
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var suffixGroup in suffixGroups)
            {
                var tabName = suffixGroup.Key == "기타" ? "📊 기타" : $"📊 {suffixGroup.Key}";
                var tabConfig = new FLTabConfig
                {
                    Name = tabName,
                    IsEnabled = true,
                    IsIntegrated = false
                };

                // 3단계: 업무영역+액션으로 세부 그룹 생성
                var businessActionGroups = GroupByBusinessAction(suffixGroup.ToList());

                foreach (var baGroup in businessActionGroups.OrderBy(g => g.Key))
                {
                    var tagGroup = new FLTagGroup
                    {
                        GroupName = baGroup.Key
                    };

                    // 시퀀스 순서 결정
                    var orderedTags = OrderTagsBySequence(baGroup.Value);
                    
                    int order = 1;
                    foreach (var tagInfo in orderedTags)
                    {
                        tagGroup.Tags.Add(new FLTagItem
                        {
                            TagName = tagInfo.TagName,
                            ValueFilter = tagInfo.Value,
                            Order = order++
                        });
                    }

                    if (tagGroup.Tags.Count > 0)
                    {
                        tabConfig.TagGroups.Add(tagGroup);
                    }
                }

                if (tabConfig.TagGroups.Count > 0)
                {
                    result.Tabs.Add(tabConfig);
                }
            }

            // 4단계: 태그 설명 자동 생성
            var uniqueTags = allTags.Select(t => t.TagName).Distinct().ToList();
            int tagOrder = 1;
            foreach (var tagName in uniqueTags)
            {
                var info = allTags.First(t => t.TagName == tagName);
                var description = GenerateTagDescription(info);
                result.TagConfigs.Add(new FLTagConfig
                {
                    TagName = tagName,
                    DisplayName = description,
                    IsEnabled = true,
                    Order = tagOrder++
                });
            }

            // 5단계: 요약 생성
            var totalGroups = result.Tabs.Sum(t => t.TagGroups.Count);
            var totalTags = result.Tabs.Sum(t => t.TagItems.Count);
            result.Summary = $"분석 완료: {result.Tabs.Count}개 탭, {totalGroups}개 그룹, {totalTags}개 태그 조건";

            return result;
        }

        /// <summary>
        /// 단일 태그명 분석
        /// </summary>
        public static TagAnalysisInfo AnalyzeTagName(string tagName, string value = "")
        {
            var info = new TagAnalysisInfo
            {
                TagName = tagName,
                Value = value
            };

            // 접미사 추출 (_01, _02)
            var suffixMatch = SuffixPattern.Match(tagName);
            if (suffixMatch.Success)
            {
                info.Suffix = "_" + suffixMatch.Groups[1].Value;
            }

            // IO 방향 추출
            if (tagName.StartsWith("I_", StringComparison.OrdinalIgnoreCase))
            {
                info.IoDirection = "I";
                info.IoDirectionDisplay = "IN";
            }
            else if (tagName.StartsWith("O_", StringComparison.OrdinalIgnoreCase))
            {
                info.IoDirection = "O";
                info.IoDirectionDisplay = "OUT";
            }

            // 데이터 타입 추출
            if (tagName.Contains("_LB_", StringComparison.OrdinalIgnoreCase))
            {
                info.DataType = "LB";
                info.DataTypeDisplay = "Boolean";
            }
            else if (tagName.Contains("_LW_", StringComparison.OrdinalIgnoreCase))
            {
                info.DataType = "LW";
                info.DataTypeDisplay = "Word";
            }
            else if (tagName.Contains("_LD_", StringComparison.OrdinalIgnoreCase))
            {
                info.DataType = "LD";
                info.DataTypeDisplay = "DWord";
            }

            // 종류 추출 (EVENT / DATA)
            if (tagName.Contains("_EVENT_", StringComparison.OrdinalIgnoreCase))
            {
                info.Kind = "EVENT";
            }
            else if (tagName.Contains("_DATA_", StringComparison.OrdinalIgnoreCase))
            {
                info.Kind = "DATA";
            }

            // 액션 추출 (CONFIRM 여부 포함)
            if (tagName.Contains("_CONFIRM_", StringComparison.OrdinalIgnoreCase) ||
                tagName.Contains("_CONFIRM_", StringComparison.OrdinalIgnoreCase))
            {
                info.IsConfirm = true;
                
                // REQUEST_CONFIRM 또는 SEND_CONFIRM 형태 확인
                if (tagName.Contains("REQUEST_CONFIRM", StringComparison.OrdinalIgnoreCase) ||
                    tagName.Contains("REQUEST", StringComparison.OrdinalIgnoreCase) && tagName.Contains("CONFIRM", StringComparison.OrdinalIgnoreCase))
                {
                    info.Action = "REQUEST";
                }
                else if (tagName.Contains("SEND_CONFIRM", StringComparison.OrdinalIgnoreCase) ||
                         tagName.Contains("SEND", StringComparison.OrdinalIgnoreCase) && tagName.Contains("CONFIRM", StringComparison.OrdinalIgnoreCase))
                {
                    info.Action = "SEND";
                }
                else if (tagName.Contains("REPORT_CONFIRM", StringComparison.OrdinalIgnoreCase))
                {
                    info.Action = "REPORT";
                }
                else
                {
                    info.Action = "CONFIRM";
                }
            }
            else
            {
                // 일반 액션
                foreach (var action in ActionKeywords)
                {
                    if (tagName.Contains($"_{action}_", StringComparison.OrdinalIgnoreCase) ||
                        tagName.EndsWith($"_{action}_" + info.Suffix.TrimStart('_'), StringComparison.OrdinalIgnoreCase))
                    {
                        info.Action = action;
                        break;
                    }
                }
            }

            // 업무영역 추출
            info.Business = ExtractBusiness(tagName, info);

            // 그룹 키 생성 (업무영역_액션)
            if (!string.IsNullOrEmpty(info.Business) && !string.IsNullOrEmpty(info.Action))
            {
                info.GroupKey = $"{info.Business}_{info.Action}";
            }
            else if (!string.IsNullOrEmpty(info.Business))
            {
                info.GroupKey = info.Business;
            }
            else if (!string.IsNullOrEmpty(info.Action))
            {
                info.GroupKey = info.Action;
            }
            else
            {
                info.GroupKey = "기타";
            }

            return info;
        }

        /// <summary>
        /// 업무영역 추출
        /// </summary>
        private static string ExtractBusiness(string tagName, TagAnalysisInfo info)
        {
            // 태그명에서 IO, TYPE, KIND, ACTION, SUFFIX를 제거하고 BUSINESS 추출
            var workingName = tagName;

            // 접두사 제거 (I_LB_EVENT_, O_LW_DATA_ 등)
            var prefixPatterns = new[]
            {
                @"^[IO]_L[BWD]_EVENT_",
                @"^[IO]_L[BWD]_DATA_",
                @"^[IO]_L[BWD]_"
            };

            foreach (var pattern in prefixPatterns)
            {
                var match = Regex.Match(workingName, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    workingName = workingName.Substring(match.Length);
                    break;
                }
            }

            // 접미사 및 액션 제거
            workingName = Regex.Replace(workingName, @"_(REQUEST|SEND|REPORT)_CONFIRM_\d{2}$", "", RegexOptions.IgnoreCase);
            workingName = Regex.Replace(workingName, @"_(REQUEST|SEND|CONFIRM|REPORT)_\d{2}$", "", RegexOptions.IgnoreCase);
            workingName = Regex.Replace(workingName, @"_\d{2}$", "");

            // 알려진 업무영역과 매칭
            foreach (var business in BusinessKeywords)
            {
                if (workingName.StartsWith(business, StringComparison.OrdinalIgnoreCase))
                {
                    return business;
                }
            }

            // 매칭되지 않으면 추출된 이름 반환
            return workingName.Length > 0 ? workingName : "";
        }

        /// <summary>
        /// 업무영역+액션으로 그룹화
        /// </summary>
        private static Dictionary<string, List<TagAnalysisInfo>> GroupByBusinessAction(List<TagAnalysisInfo> tags)
        {
            var groups = new Dictionary<string, List<TagAnalysisInfo>>();

            foreach (var tag in tags)
            {
                var groupName = tag.GroupKey;
                if (string.IsNullOrEmpty(groupName))
                    groupName = "기타";

                if (!groups.ContainsKey(groupName))
                {
                    groups[groupName] = new List<TagAnalysisInfo>();
                }

                groups[groupName].Add(tag);
            }

            return groups;
        }

        /// <summary>
        /// 시퀀스 순서 결정
        /// 
        /// REQUEST 시퀀스:
        /// 1. I_LB_EVENT_xxx_REQUEST [ON]     - 요청 시작
        /// 2. I_LW_DATA_xxx_REQUEST           - 요청 데이터
        /// 3. O_LB_EVENT_xxx_REQUEST_CONFIRM [ON] - 확인 응답
        /// 4. I_LB_EVENT_xxx_REQUEST [OFF]    - 요청 종료
        /// 5. O_LB_EVENT_xxx_REQUEST_CONFIRM [OFF] - 확인 종료
        /// 
        /// SEND 시퀀스:
        /// 1. O_LW_DATA_xxx_SEND              - 전송 데이터
        /// 2. O_LB_EVENT_xxx_SEND [ON]        - 전송 시작
        /// 3. I_LB_EVENT_xxx_SEND_CONFIRM [ON] - 확인 응답
        /// 4. O_LB_EVENT_xxx_SEND [OFF]       - 전송 종료
        /// 5. I_LB_EVENT_xxx_SEND_CONFIRM [OFF] - 확인 종료
        /// </summary>
        private static List<TagAnalysisInfo> OrderTagsBySequence(List<TagAnalysisInfo> tags)
        {
            int GetOrderScore(TagAnalysisInfo tag)
            {
                int score = 0;
                bool isOn = tag.Value.Equals("ON", StringComparison.OrdinalIgnoreCase);
                bool isOff = tag.Value.Equals("OFF", StringComparison.OrdinalIgnoreCase);
                bool isInput = tag.IoDirection == "I";
                bool isOutput = tag.IoDirection == "O";
                bool isEvent = tag.Kind == "EVENT";
                bool isData = tag.Kind == "DATA";
                bool isConfirm = tag.IsConfirm;
                bool isRequest = tag.Action == "REQUEST";
                bool isSend = tag.Action == "SEND";
                bool isReport = tag.Action == "REPORT";

                // REQUEST 시퀀스 (I → O)
                if (isRequest)
                {
                    if (!isConfirm)
                    {
                        // I_LB_EVENT_REQUEST ON → 0
                        // I_LW_DATA_REQUEST → 100
                        // I_LB_EVENT_REQUEST OFF → 2000
                        if (isEvent && isOn) score = 0;
                        else if (isData) score = 100;
                        else if (isEvent && isOff) score = 2000;
                        else score = 50;
                    }
                    else
                    {
                        // O_LB_EVENT_REQUEST_CONFIRM ON → 200
                        // O_LB_EVENT_REQUEST_CONFIRM OFF → 2100
                        if (isOn) score = 200;
                        else if (isOff) score = 2100;
                        else score = 300;
                    }
                }
                // SEND 시퀀스 (O → I)
                else if (isSend)
                {
                    if (!isConfirm)
                    {
                        // O_LW_DATA_SEND → 0
                        // O_LB_EVENT_SEND ON → 100
                        // O_LB_EVENT_SEND OFF → 2000
                        if (isData) score = 0;
                        else if (isEvent && isOn) score = 100;
                        else if (isEvent && isOff) score = 2000;
                        else score = 50;
                    }
                    else
                    {
                        // I_LB_EVENT_SEND_CONFIRM ON → 200
                        // I_LB_EVENT_SEND_CONFIRM OFF → 2100
                        if (isOn) score = 200;
                        else if (isOff) score = 2100;
                        else score = 300;
                    }
                }
                // REPORT 시퀀스
                else if (isReport)
                {
                    if (!isConfirm)
                    {
                        if (isData) score = 0;
                        else if (isEvent && isOn) score = 100;
                        else if (isEvent && isOff) score = 2000;
                        else score = 50;
                    }
                    else
                    {
                        if (isOn) score = 200;
                        else if (isOff) score = 2100;
                        else score = 300;
                    }
                }
                // 기타 (일반적인 순서)
                else
                {
                    if (isInput) score += 0;
                    else if (isOutput) score += 500;

                    if (isOn) score += 0;
                    else if (isOff) score += 1000;

                    if (isData) score += 0;
                    else if (isEvent) score += 10;
                    
                    if (isConfirm) score += 100;
                }

                return score;
            }

            return tags.OrderBy(t => GetOrderScore(t)).ToList();
        }

        /// <summary>
        /// 태그 설명 자동 생성
        /// </summary>
        private static string GenerateTagDescription(TagAnalysisInfo info)
        {
            var parts = new List<string>();

            // IO 방향
            if (!string.IsNullOrEmpty(info.IoDirectionDisplay))
                parts.Add(info.IoDirectionDisplay);

            // 데이터 타입
            if (!string.IsNullOrEmpty(info.DataTypeDisplay))
                parts.Add(info.DataTypeDisplay);

            // 종류
            if (info.Kind == "EVENT")
                parts.Add("이벤트");
            else if (info.Kind == "DATA")
                parts.Add("데이터");

            // 업무영역 (간략화)
            if (!string.IsNullOrEmpty(info.Business))
            {
                var businessDisplay = info.Business
                    .Replace("_", " ")
                    .Replace("LOT INFO", "LOT정보")
                    .Replace("LOT CONFIRM DATA", "LOT확인데이터")
                    .Replace("LOT PROCESSING COMPLETED REPORT", "LOT처리완료보고")
                    .Replace("MATERIAL INSPECTION REPORT", "자재검사보고")
                    .Replace("REASON CODE", "사유코드");
                parts.Add(businessDisplay);
            }

            // 액션
            if (info.IsConfirm)
                parts.Add("확인");
            else if (info.Action == "REQUEST")
                parts.Add("요청");
            else if (info.Action == "SEND")
                parts.Add("전송");
            else if (info.Action == "REPORT")
                parts.Add("보고");

            return parts.Count > 0 ? string.Join(" ", parts) : "";
        }

        /// <summary>
        /// 패턴 미리보기 생성
        /// </summary>
        public static string GeneratePreview(AnalysisResult result)
        {
            var lines = new List<string>();

            lines.Add($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            lines.Add($"📊 자동 분석 결과");
            lines.Add($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            lines.Add("");

            foreach (var pattern in result.DiscoveredPatterns)
            {
                lines.Add($"  📌 {pattern}");
            }
            lines.Add("");

            foreach (var tab in result.Tabs)
            {
                lines.Add($"📁 탭: {tab.Name}");
                
                foreach (var group in tab.TagGroups)
                {
                    lines.Add($"   📂 그룹: {group.GroupName}");
                    
                    foreach (var tag in group.Tags)
                    {
                        var valueStr = string.IsNullOrEmpty(tag.ValueFilter) ? "" : $" [{tag.ValueFilter}]";
                        lines.Add($"      #{tag.Order} {tag.TagName}{valueStr}");
                    }
                }
                lines.Add("");
            }

            lines.Add($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            lines.Add(result.Summary);

            return string.Join(Environment.NewLine, lines);
        }
    }
}
