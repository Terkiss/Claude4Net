using System;
using System.Collections.Generic;
using System.Text;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SystemPromptBuilder
    {
        public string Build(string providerName, string? taskContext = null)
        {
            var sb = new StringBuilder();

            // 1. Global Identity & Base Protocol
            sb.AppendLine("# Claude4Net Global System Protocol");
            sb.AppendLine($"Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Working Directory: {AppState.CurrentCwd}");
            sb.AppendLine();
            sb.AppendLine("당신은 사용자의 로컬 시스템과 완벽하게 동기화된 인텔리전트 에이전트입니다.");
            sb.AppendLine("제공된 도구를 활용하여 파일 조작, 시스템 관리, 코드 실행 요청을 자율적으로 완수하십시오.");
            sb.AppendLine();

            // 2. Provider Specific Identity
            if (providerName.ToLower() == "gemini")
            {
                sb.AppendLine("## Gemini Agent Protocol (Antigravity Mode)");
                sb.AppendLine("- Deep Think & Tool Execution: 요청을 받으면 즉시 도구를 호출하여 상태를 확인하십시오.");
                sb.AppendLine("- Zero-Hallucination: 실제 결과가 반환되기 전까지 추측하지 마십시오.");
            }
            else if (providerName.ToLower() == "ollama")
            {
                sb.AppendLine("## Ollama Local Agent Protocol");
                sb.AppendLine("- 도구 우선주의: 대답하기 전에 반드시 제공된 도구를 먼저 실행하십시오.");
            }

            sb.AppendLine();

            // 3. Task Context (Optional)
            if (!string.IsNullOrEmpty(taskContext))
            {
                sb.AppendLine("## Current Task Context");
                sb.AppendLine(taskContext);
                sb.AppendLine();
            }

            // 4. Common Tool Usage Rules
            sb.AppendLine("## Tool Execution Rules");
            sb.AppendLine("1. [Analyze] 요청 분석 및 필요 도구 결정.");
            sb.AppendLine("2. [Execute] 즉시 도구 호출 (불필요한 설명 생략).");
            sb.AppendLine("3. [Verify] 결과 확인 및 필요시 스스로 디버깅하여 재시도.");
            sb.AppendLine();
            
            return sb.ToString();
        }
    }
}
