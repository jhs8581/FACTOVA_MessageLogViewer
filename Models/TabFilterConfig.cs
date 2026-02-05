using System;
using System.Collections.Generic;
using System.Linq;

namespace FACTOVA_MessageLogViewer.Models
{
    /// <summary>
    /// 탭 필터링을 위한 개별 조건
    /// </summary>
    public class TabFilterCondition
    {
        /// <summary>
        /// 조건 대상 필드명 (예: MSGID, WORK_TYPE, RETURN_CODE 등)
        /// "MSGID"는 특수 처리 (LogEntry.MessageId 참조)
        /// IsKeywordSearch가 true면 무시됨
        /// </summary>
        public string FieldName { get; set; } = "";

        /// <summary>
        /// 필터 값 (해당 필드가 이 값과 일치해야 함)
        /// 여러 값 허용 시 쉼표로 구분 (OR 조건)
        /// IsKeywordSearch가 true면 전체 로그에서 검색할 키워드
        /// </summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// 값이 정확히 일치해야 하는지, 포함되면 되는지
        /// </summary>
        public bool ExactMatch { get; set; } = true;

        /// <summary>
        /// 키워드 검색 모드 (필드 무시하고 전체 로그에서 검색)
        /// true면 FieldName 무시, Value를 RawData에서 Contains 검색
        /// </summary>
        public bool IsKeywordSearch { get; set; } = false;

        /// <summary>
        /// 디스플레이 명칭 (쉼표 구분, Value와 순서 매핑)
        /// 예: Value="2,3", DisplayNames="RR,R" → 2→RR, 3→R
        /// </summary>
        public string DisplayNames { get; set; } = "";

        /// <summary>
        /// Value 값을 DisplayNames로 변환 (없으면 원래 값 반환)
        /// </summary>
        public string GetDisplayValue(string value)
        {
            if (string.IsNullOrEmpty(DisplayNames))
                return value;

            var values = Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(v => v.Trim())
                              .ToList();
            var displayNames = DisplayNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(v => v.Trim())
                                           .ToList();

            int index = values.FindIndex(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < displayNames.Count)
                return displayNames[index];

            return value;
        }

        /// <summary>
        /// 전체 Value에 대한 디스플레이 문자열 반환
        /// </summary>
        public string GetDisplayString()
        {
            if (string.IsNullOrEmpty(Value))
                return "";

            var values = Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(v => v.Trim())
                              .ToList();
            var displayNames = DisplayNames?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Select(v => v.Trim())
                                            .ToList() ?? new List<string>();

            var result = new List<string>();
            for (int i = 0; i < values.Count; i++)
            {
                if (i < displayNames.Count && !string.IsNullOrEmpty(displayNames[i]))
                    result.Add(displayNames[i]);
                else
                    result.Add(values[i]);
            }

            return string.Join(",", result);
        }

        /// <summary>
        /// 조건 검사
        /// </summary>
        public bool IsMatch(LogEntry entry)
        {
            if (entry == null)
                return true;

            // 키워드 검색 모드: 전체 RawData에서 검색
            if (IsKeywordSearch)
            {
                if (string.IsNullOrEmpty(Value))
                    return true;

                // 키워드들 (줄바꿈 또는 쉼표로 구분)
                var keywords = Value.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(v => v.Trim())
                                    .Where(v => !string.IsNullOrEmpty(v))
                                    .ToList();

                if (keywords.Count == 0)
                    return true;

                // 하나라도 포함되면 매칭 (OR)
                foreach (var keyword in keywords)
                {
                    if (entry.RawData.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            // 기존 필드 기반 검색
            if (string.IsNullOrEmpty(FieldName))
                return true;

            // 여러 값이 있으면 OR 조건으로 처리
            var values = Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(v => v.Trim())
                              .Where(v => !string.IsNullOrEmpty(v))
                              .ToList();

            if (values.Count == 0)
                return true;

            string fieldValue = GetFieldValue(entry);

            foreach (var val in values)
            {
                if (ExactMatch)
                {
                    if (string.Equals(fieldValue, val, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (fieldValue.Contains(val, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// DATA 로그 조건 검사
        /// </summary>
        public bool IsMatch(DataLogEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(FieldName))
                return true;

            // SLOW_QUERY 특수 처리: 실행시간 기반 필터
            if (FieldName.Equals("SLOW_QUERY", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(Value, out double thresholdMs))
                {
                    return entry.ExecTimeMs >= thresholdMs;
                }
                return false;
            }

            var values = Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(v => v.Trim())
                              .Where(v => !string.IsNullOrEmpty(v))
                              .ToList();

            if (values.Count == 0)
                return true;

            string fieldValue = GetDataFieldValue(entry);

            foreach (var val in values)
            {
                if (ExactMatch)
                {
                    if (string.Equals(fieldValue, val, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else
                {
                    if (fieldValue.Contains(val, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private string GetFieldValue(LogEntry entry)
        {
            // 특수 필드 처리
            return FieldName.ToUpperInvariant() switch
            {
                "MSGID" or "MESSAGEID" => entry.MessageId ?? "",
                "DIRECTION" => entry.Direction ?? "",
                "RETURN_CODE" or "RETURNCODE" => entry.ReturnCode ?? "",
                "WORK_TYPE" or "WORKTYPE" => entry.WorkType ?? "",
                "ERROR_CODE" or "ERRORCODE" => entry.ErrorCode ?? "",
                _ => entry.Fields?.GetValueOrDefault(FieldName, "") ?? ""
            };
        }

        private string GetDataFieldValue(DataLogEntry entry)
        {
            // DATA 로그 특수 필드 처리
            return FieldName.ToUpperInvariant() switch
            {
                "BIZNAME" or "BIZ_NAME" => entry.BizName ?? "",
                "TXNID" or "TXN_ID" => entry.TxnId ?? "",
                "EXECTIME" or "EXEC_TIME" => entry.ExecTime ?? "",
                "CLIENT_ID" or "CLIENTID" => entry.ClientId ?? "",
                "CLIENT_IP" or "CLIENTIP" => entry.ClientIp ?? "",
                "EQUIPMENT_ID" or "EQUIPMENTID" => entry.EquipmentId ?? "",
                "SFC_MODE" or "SFCMODE" => entry.SfcMode ?? "",
                _ => entry.Fields?.GetValueOrDefault(FieldName, "") ?? ""
            };
        }
    }

    /// <summary>
    /// 조건 그룹 (그룹 내 조건들은 AND로 적용)
    /// </summary>
    public class ConditionGroup
    {
        /// <summary>
        /// 그룹 이름 (예: "2000번 스캔", "1100번 도어")
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 그룹 내 조건들 (AND 조건)
        /// </summary>
        public List<TabFilterCondition> Conditions { get; set; } = new();

        /// <summary>
        /// 그룹 조건 검사 (모든 조건 AND) - EVENT 로그
        /// </summary>
        public bool IsMatch(LogEntry entry)
        {
            if (entry == null)
                return false;

            if (Conditions == null || Conditions.Count == 0)
                return true;

            return Conditions.All(c => c.IsMatch(entry));
        }

        /// <summary>
        /// 그룹 조건 검사 (모든 조건 AND) - DATA 로그
        /// </summary>
        public bool IsMatch(DataLogEntry entry)
        {
            if (entry == null)
                return false;

            if (Conditions == null || Conditions.Count == 0)
                return true;

            return Conditions.All(c => c.IsMatch(entry));
        }

        /// <summary>
        /// 조건 요약
        /// </summary>
        public string Summary
        {
            get
            {
                if (Conditions == null || Conditions.Count == 0)
                    return "(조건 없음)";

                var parts = Conditions
                    .Where(c => !string.IsNullOrEmpty(c.FieldName) && !string.IsNullOrEmpty(c.Value))
                    .Select(c => $"{c.FieldName}={c.GetDisplayString()}");

                return string.Join(" AND ", parts);
            }
        }
    }

    /// <summary>
    /// 개별 탭 설정
    /// </summary>
    public class TabConfig
    {
        /// <summary>
        /// 탭 표시 이름
        /// </summary>
        public string Name { get; set; } = "새 탭";

        /// <summary>
        /// 탭 순서 (낮을수록 앞에 표시)
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// [구버전 호환] 단일 조건 목록 (AND 조건)
        /// </summary>
        public List<TabFilterCondition> Conditions { get; set; } = new();

        /// <summary>
        /// [신규] 조건 그룹 목록 (그룹 간 OR, 그룹 내 AND)
        /// </summary>
        public List<ConditionGroup> ConditionGroups { get; set; } = new();

        /// <summary>
        /// 탭 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 통합 로그 탭인지 여부 (통합 탭은 모든 로그 표시)
        /// </summary>
        public bool IsIntegrated { get; set; } = false;

        /// <summary>
        /// 로그 엔트리가 이 탭의 조건에 맞는지 검사 - EVENT 로그
        /// </summary>
        public bool IsMatch(LogEntry entry)
        {
            if (entry == null)
                return false;

            // 통합 탭은 모든 로그 표시
            if (IsIntegrated)
                return true;

            // 조건 그룹이 있으면 OR 로직 사용
            if (ConditionGroups != null && ConditionGroups.Count > 0)
            {
                // 그룹 중 하나라도 만족하면 OK (OR)
                return ConditionGroups.Any(g => g.IsMatch(entry));
            }

            // 구버전 호환: 단일 조건 목록 (AND)
            if (Conditions == null || Conditions.Count == 0)
                return true;

            return Conditions.All(c => c.IsMatch(entry));
        }

        /// <summary>
        /// 로그 엔트리가 이 탭의 조건에 맞는지 검사 - DATA 로그
        /// </summary>
        public bool IsMatch(DataLogEntry entry)
        {
            if (entry == null)
                return false;

            // 통합 탭은 모든 로그 표시
            if (IsIntegrated)
                return true;

            // 조건 그룹이 있으면 OR 로직 사용
            if (ConditionGroups != null && ConditionGroups.Count > 0)
            {
                return ConditionGroups.Any(g => g.IsMatch(entry));
            }

            // 구버전 호환: 단일 조건 목록 (AND)
            if (Conditions == null || Conditions.Count == 0)
                return true;

            return Conditions.All(c => c.IsMatch(entry));
        }

        /// <summary>
        /// 조건 요약 문자열
        /// </summary>
        public string ConditionSummary
        {
            get
            {
                if (IsIntegrated)
                    return "모든 로그";

                // 조건 그룹이 있으면 그룹 요약
                if (ConditionGroups != null && ConditionGroups.Count > 0)
                {
                    var groupSummaries = ConditionGroups
                        .Where(g => g.Conditions?.Count > 0)
                        .Select(g => $"({g.Summary})");

                    return string.Join(" OR ", groupSummaries);
                }

                // 구버전 호환
                if (Conditions == null || Conditions.Count == 0)
                    return "조건 없음";

                var parts = Conditions
                    .Where(c => !string.IsNullOrEmpty(c.FieldName) && !string.IsNullOrEmpty(c.Value))
                    .Select(c => $"{c.FieldName}={c.Value}");

                return string.Join(" AND ", parts);
            }
        }

        /// <summary>
        /// 매칭된 그룹명 반환 - EVENT 로그
        /// </summary>
        public string? GetMatchedGroupName(LogEntry entry)
        {
            if (entry == null || IsIntegrated)
                return null;

            // 조건 그룹이 있으면 매칭된 첫 번째 그룹명 반환
            if (ConditionGroups != null && ConditionGroups.Count > 0)
            {
                var matchedGroup = ConditionGroups.FirstOrDefault(g => g.IsMatch(entry));
                if (matchedGroup != null && !string.IsNullOrEmpty(matchedGroup.Name))
                    return matchedGroup.Name;
            }

            return null;
        }

        /// <summary>
        /// 매칭된 그룹명 반환 - DATA 로그
        /// </summary>
        public string? GetMatchedGroupName(DataLogEntry entry)
        {
            if (entry == null || IsIntegrated)
                return null;

            // 조건 그룹이 있으면 매칭된 첫 번째 그룹명 반환
            if (ConditionGroups != null && ConditionGroups.Count > 0)
            {
                var matchedGroup = ConditionGroups.FirstOrDefault(g => g.IsMatch(entry));
                if (matchedGroup != null && !string.IsNullOrEmpty(matchedGroup.Name))
                    return matchedGroup.Name;
            }

            return null;
        }
    }

    /// <summary>
    /// 탭 설정 전체 (프리셋에 포함)
    /// </summary>
    public class TabSettings
    {
        /// <summary>
        /// 탭 목록
        /// </summary>
        public List<TabConfig> Tabs { get; set; } = new();

        /// <summary>
        /// 마지막 선택된 탭 인덱스
        /// </summary>
        public int LastSelectedTabIndex { get; set; } = 0;

        /// <summary>
        /// 기본 탭 설정 생성 - EVENT 로그용
        /// </summary>
        public static TabSettings CreateDefault()
        {
            return new TabSettings
            {
                Tabs = new List<TabConfig>
                {
                    new TabConfig
                    {
                        Name = "통합 로그",
                        Order = 0,
                        IsIntegrated = true,
                        IsEnabled = true
                    }
                }
            };
        }

        /// <summary>
        /// 기본 탭 설정 생성 - DATA 로그용
        /// </summary>
        public static TabSettings CreateDataDefault()
        {
            return new TabSettings
            {
                Tabs = new List<TabConfig>
                {
                    new TabConfig
                    {
                        Name = "통합 로그",
                        Order = 0,
                        IsIntegrated = true,
                        IsEnabled = true
                    }
                }
            };
        }

        /// <summary>
        /// 활성화된 탭만 반환
        /// </summary>
        public IEnumerable<TabConfig> EnabledTabs => Tabs?.Where(t => t.IsEnabled).OrderBy(t => t.Order) ?? Enumerable.Empty<TabConfig>();
    }
}
