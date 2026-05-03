using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 민감 정보 필터링 결과를 담는 모델입니다.
    /// </summary>
    public class RedactionResult
    {
        /// <summary> 원본 텍스트 </summary>
        public string OriginalText { get; set; } = string.Empty;
        /// <summary> 필터링된(마스킹된) 텍스트 </summary>
        public string FilteredText { get; set; } = string.Empty;
        /// <summary> 발견된 민감 정보 유형 목록 (예: API Key, Email) </summary>
        public List<string> FoundTypes { get; set; } = new();
        /// <summary> 총 매칭 횟수 </summary>
        public int TotalMatches { get; set; }
        /// <summary> 민감 정보가 발견되지 않았는지 여부 </summary>
        public bool IsClean => TotalMatches == 0;
    }

    /// <summary>
    /// 로그나 출력물에서 API 키, 비밀번호 등 민감 정보를 탐지하고 마스킹하는 보안 유틸리티입니다.
    /// </summary>
    public static class SourceGuard
    {
        // 민감 정보 탐지를 위한 정규식 필터 목록
        private static readonly List<(string Name, Regex Pattern)> _filters = new()
        {
            ("API Key", new Regex(@"\b(sk-ant-[a-zA-Z0-9_\-]{16,}|sk-[a-zA-Z0-9]{20,}|AIza[0-9A-Za-z_\-]{20,}|gh[pousr]_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled)),
            ("AWS Access Key", new Regex(@"\b(AKIA[0-9A-Z]{16})\b", RegexOptions.Compiled)),
            ("AWS Secret Key", new Regex(@"\b([a-zA-Z0-9/+=]{40})\b", RegexOptions.Compiled)), 
            ("Discord Token", new Regex(@"([a-zA-Z0-9_\-]{24}\.[a-zA-Z0-9_\-]{6}\.[a-zA-Z0-9_\-]{27})", RegexOptions.Compiled)),
            ("Authorization Bearer", new Regex(@"(Bearer\s+[a-zA-Z0-9\-\._~+/]+=*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("SSH Private Key", new Regex(@"-----BEGIN [A-Z ]+ PRIVATE KEY-----[\s\S]+?-----END [A-Z ]+ PRIVATE KEY-----", RegexOptions.Compiled)),
            ("Connection String Password", new Regex(@"(password|pwd|pwd|secret|key)\s*=\s*([^;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Generic Secret", new Regex(@"(password|pass|secret|token|key)\s*[:=]\s*([^\s,;\""\'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Email", new Regex(@"([a-zA-Z0-9_\-\.]+)@([a-zA-Z0-9_\-\.]+)\.([a-zA-Z]{2,5})", RegexOptions.Compiled))
        };

        // 민감한 것으로 간주되는 키 이름의 키워드 목록
        private static readonly string[] _sensitiveKeyParts =
        {
            "KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "PWD", "AUTH", 
            "CONNECTION", "CREDENTIAL", "DATABASE", "CERTIFICATE", "PRIVATE", 
            "API", "LICENSE"
        };

        /// <summary>
        /// 입력 텍스트에서 민감 정보를 찾아 마스킹 처리합니다.
        /// </summary>
        /// <param name="input">검사할 입력 문자열</param>
        /// <returns>필터링 결과 객체</returns>
        public static RedactionResult Filter(string? input)
        {
            var result = new RedactionResult { OriginalText = input ?? "" };
            if (string.IsNullOrEmpty(input))
            {
                result.FilteredText = "";
                return result;
            }

            string filtered = input;
            // 등록된 모든 필터를 순회하며 매칭 확인
            foreach (var filter in _filters)
            {
                var matches = filter.Pattern.Matches(filtered);
                if (matches.Count > 0)
                {
                    result.TotalMatches += matches.Count;
                    if (!result.FoundTypes.Contains(filter.Name))
                        result.FoundTypes.Add(filter.Name);

                    // 매칭된 부분을 마스킹 문자로 교체
                    filtered = filter.Pattern.Replace(filtered, m => 
                    {
                        // 'password=value' 형식인 경우 'password=' 부분은 보존하고 값만 마스킹
                        if (m.Groups.Count > 1 && (filter.Name.Contains("Generic") || filter.Name.Contains("Connection")))
                        {
                             return m.Groups[1].Value + "=****";
                        }
                        return "****";
                    });
                }
            }

            result.FilteredText = filtered;
            return result;
        }

        /// <summary>
        /// 특정 값이나 키-값 쌍을 판단하여 마스킹된 문자열을 반환합니다.
        /// </summary>
        /// <param name="value">값</param>
        /// <param name="keyName">값이 속한 키 이름 (선택 사항)</param>
        /// <returns>마스킹된 결과 문자열</returns>
        public static string MaskValue(string? value, string? keyName = null)
        {
            if (string.IsNullOrEmpty(value)) return "(not set)";
            
            // 1. 패턴 기반 필터링 수행
            var result = Filter(value);
            if (!result.IsClean) return result.FilteredText;

            // 2. 키 이름 기반의 휴리스틱 검사 (예: 키 이름에 'PASS'가 포함된 경우)
            if (LooksSensitiveKey(keyName))
                return SecurityUtils.Mask(value);

            return value;
        }

        /// <summary>
        /// 키 이름이 보안상 민감한 정보를 담고 있을 가능성이 있는지 확인합니다.
        /// </summary>
        /// <param name="keyName">검사할 키 이름</param>
        /// <returns>민감해 보이면 true</returns>
        public static bool LooksSensitiveKey(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            // 키워드 목록 중 하나라도 포함되어 있는지 대소문자 구분 없이 확인
            string normalized = keyName.ToUpperInvariant();
            return _sensitiveKeyParts.Any(part => normalized.Contains(part));
        }
    }
}
