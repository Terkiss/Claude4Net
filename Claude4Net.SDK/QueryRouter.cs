using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 입력 쿼리를 분석하여 LLM으로 보낼지, 혹은 시스템 내부 명령어로 처리할지 결정하는 라우터입니다.
    /// </summary>
    public class QueryRouter
    {
        /// <summary>
        /// 입력을 분석하여 시스템 명령어(예: !build)로 매핑하거나, LLM 처리를 위해 null을 반환합니다.
        /// </summary>
        /// <param name="input">사용자 입력 텍스트</param>
        /// <returns>매핑된 시스템 명령어 문자열, 혹은 LLM 처리가 필요한 경우 null</returns>
        public static string? Route(string input)
        {
            string text = input.Trim().ToLower();

            // 긴 자연어 프롬프트가 단어 하나 때문에 시스템 명령어로 가로채지는 현상 방지 (20자 초과 시 LLM으로 전달)
            if (text.Length > 20) return null;

            // 특정 키워드에 대한 시스템 명령어 매핑
            // 'build' 또는 '빌드' 포함 시 빌드 명령어로 전환
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(build|빌드)\b")) return "!build";
            // 'test' 또는 '테스트' 포함 시 테스트 명령어로 전환
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(test|테스트)\b")) return "!test";
            // 'clean' 관련 키워드 처리
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(clean|클린|청소)\b")) return "!clean";
            // 'status' 관련 키워드 처리
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(status|상태)\b")) return "!status";
            
            return null; // 매칭되는 명령어가 없으면 LLM이 처리하도록 함
        }
    }
}
