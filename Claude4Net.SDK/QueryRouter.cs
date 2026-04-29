using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    public class QueryRouter
    {
        public static string? Route(string input)
        {
            string text = input.Trim().ToLower();

            // 긴 자연어 프롬프트가 단어 하나 때문에 시스템 명령어로 가로채지는 현상 방지
            if (text.Length > 20) return null;

            // Simple Keyword Mapping to System Commands
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(build|빌드)\b")) return "!build";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(test|테스트)\b")) return "!test";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(clean|클린|청소)\b")) return "!clean";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(status|상태)\b")) return "!status";
            
            return null; // No match, proceed to LLM
        }
    }
}
