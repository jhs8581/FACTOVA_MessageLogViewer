using FACTOVA_MessageLogViewer.Converters;
using FACTOVA_MessageLogViewer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FACTOVA_MessageLogViewer.Helpers
{
    /// <summary>
    /// 공통 검색 헬퍼 (모든 로그 뷰어에서 사용)
    /// 복합 검색 지원: 쉼표(,) = OR, 플러스(+) = AND
    /// 예: "GetData+1234" → GetData AND 1234
    /// 예: "Insert,Update" → Insert OR Update
    /// 예: "GetData+1234,Update" → (GetData AND 1234) OR Update
    /// </summary>
    public static class SearchHelper
    {
        /// <summary>
        /// 복합 검색 매칭 (쉼표=OR, 플러스=AND)
        /// </summary>
        /// <param name="searchText">검색어</param>
        /// <param name="searchTargets">검색 대상 문자열들</param>
        /// <returns>매칭 여부</returns>
        public static bool MatchesComplexSearch(string searchText, params string[] searchTargets)
        {
            if (string.IsNullOrEmpty(searchText))
                return true;

            string searchTarget = string.Join(" ", searchTargets.Where(s => !string.IsNullOrEmpty(s)));
            return MatchesComplexSearch(searchText, searchTarget);
        }

        /// <summary>
        /// 복합 검색 매칭 (쉼표=OR, 플러스=AND)
        /// </summary>
        /// <param name="searchText">검색어</param>
        /// <param name="searchTarget">검색 대상 문자열</param>
        /// <returns>매칭 여부</returns>
        public static bool MatchesComplexSearch(string searchText, string searchTarget)
        {
            if (string.IsNullOrEmpty(searchText))
                return true;

            if (string.IsNullOrEmpty(searchTarget))
                return false;

            // 쉼표로 분리 (OR 조건)
            var orConditions = searchText.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var orCondition in orConditions)
            {
                // 플러스로 분리 (AND 조건)
                var andConditions = orCondition.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries);

                bool allMatch = true;
                foreach (var andCondition in andConditions)
                {
                    if (!searchTarget.Contains(andCondition.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        allMatch = false;
                        break;
                    }
                }

                // 하나의 OR 조건이라도 만족하면 true
                if (allMatch)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 필드 딕셔너리에서 검색 (원본 값 + ValueMapping 변환 값)
        /// </summary>
        /// <param name="searchText">검색어</param>
        /// <param name="fields">필드 딕셔너리</param>
        /// <param name="getFieldConfig">필드 설정 조회 함수 (null이면 변환 없이 원본만 검색)</param>
        /// <returns>매칭 여부</returns>
        public static bool MatchesFieldsWithMapping(
            string searchText, 
            Dictionary<string, string> fields,
            Func<string, FLFieldConfig?>? getFieldConfig = null)
        {
            if (string.IsNullOrEmpty(searchText) || fields == null || fields.Count == 0)
                return false;

            // 검색 대상 문자열 생성 (필드 키 + 원본 값 + 변환된 값)
            var searchTargetBuilder = new StringBuilder();

            foreach (var field in fields)
            {
                // 필드 키와 원본 값
                searchTargetBuilder.Append($"{field.Key} {field.Value} ");

                // 변환된 표시 값 추가 (ValueMapping 적용)
                if (getFieldConfig != null)
                {
                    var fieldConfig = getFieldConfig(field.Key);
                    if (fieldConfig != null && !string.IsNullOrEmpty(fieldConfig.ValueMapping))
                    {
                        var displayValue = fieldConfig.GetDisplayValue(field.Value);
                        if (displayValue != field.Value)
                        {
                            searchTargetBuilder.Append($"{displayValue} ");
                        }
                    }
                }
            }

            return MatchesComplexSearch(searchText, searchTargetBuilder.ToString());
        }

        /// <summary>
        /// 단순 필드 매칭 (원본 값 + ValueMapping 변환 값) - 단일 검색어
        /// </summary>
        public static bool MatchesSingleTermInFields(
            string searchTerm,
            Dictionary<string, string> fields,
            Func<string, FLFieldConfig?>? getFieldConfig = null)
        {
            if (string.IsNullOrEmpty(searchTerm) || fields == null)
                return false;

            foreach (var field in fields)
            {
                // 필드 키 검색
                if (field.Key.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 원본 값 검색
                if (field.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    return true;

                // 변환된 표시 값 검색 (ValueMapping 적용)
                if (getFieldConfig != null)
                {
                    var fieldConfig = getFieldConfig(field.Key);
                    if (fieldConfig != null && !string.IsNullOrEmpty(fieldConfig.ValueMapping))
                    {
                        var displayValue = fieldConfig.GetDisplayValue(field.Value);
                        if (displayValue != field.Value && 
                            displayValue.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 검색 대상 문자열 생성 (ValueMapping 포함)
        /// </summary>
        public static string BuildSearchTargetWithMapping<T>(
            T entry,
            Func<T, string> getBaseSearchTarget,
            Func<T, IEnumerable<(string fieldName, string? value, string? valueMapping)>>? getFieldMappings = null)
        {
            var builder = new StringBuilder();
            builder.Append(getBaseSearchTarget(entry));

            if (getFieldMappings != null)
            {
                foreach (var (fieldName, value, valueMapping) in getFieldMappings(entry))
                {
                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(valueMapping))
                    {
                        var displayValue = ValueMappingConverter.ConvertValue(value, valueMapping);
                        if (displayValue != value)
                        {
                            builder.Append($" {displayValue}");
                        }
                    }
                }
            }

            return builder.ToString();
        }
    }
}
