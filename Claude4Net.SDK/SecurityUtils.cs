using System;
using System.Text.RegularExpressions;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 보안 관련 마스킹 및 유틸리티 기능을 제공하는 클래스입니다.
    /// </summary>
    public static class SecurityUtils
    {
        /// <summary>
        /// 일반 문자열을 마스킹 처리합니다. (예: API 키의 일부만 표시)
        /// </summary>
        /// <param name="value">마스킹할 원본 문자열</param>
        /// <returns>마스킹된 문자열</returns>
        public static string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "(not set)";
            
            // 길이가 짧은 경우 (8자 이하)
            if (value.Length <= 8)
            {
                // 앞부분은 별표로 채우고 마지막 2자만 표시 (2자보다 짧으면 별표만)
                return "****" + (value.Length > 2 ? value.Substring(value.Length - 2) : "");
            }

            // 긴 토큰의 경우 처음 3자와 마지막 3자만 공개하고 중간은 생략
            return value.Substring(0, 3) + "..." + value.Substring(value.Length - 3);
        }

        /// <summary>
        /// 데이터베이스 연결 문자열 등에서 비밀번호를 마스킹합니다.
        /// </summary>
        /// <param name="connectionString">원본 연결 문자열</param>
        /// <returns>비밀번호가 마스킹된 연결 문자열</returns>
        public static string MaskConnectionString(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "(not set)";
            
            // SourceGuard의 필터링 로직을 활용하여 민감 정보 마스킹
            return SourceGuard.Filter(connectionString).FilteredText;
        }

        /// <summary>
        /// 일반 텍스트 내에 포함된 다양한 민감 정보를 탐지하여 마스킹합니다.
        /// </summary>
        /// <param name="text">검사할 전체 텍스트</param>
        /// <returns>필터링된 텍스트</returns>
        public static string MaskSensitiveInfo(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // SourceGuard를 호출하여 종합적인 마스킹 수행
            return SourceGuard.Filter(text).FilteredText;
        }
    }
}
