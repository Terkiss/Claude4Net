using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Claude4Net.SDK
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
            sb.AppendLine("## 🧠 Autonomous Memory Logging (강제 지침)");
            sb.AppendLine("당신은 '장기 기억' 메커니즘을 직접 구축해야 합니다. 중요한 대화, 사용자 지시, 또는 작업 결과가 발생할 때마다, 반드시 `pandas_insert_row` 도구를 사용하여 `agent_memory` 테이블에 기억을 저장하십시오.");
            sb.AppendLine("저장할 JSON 스키마 예시: { \"Timestamp\": \"YYYY-MM-DD HH:MM\", \"Keywords\": \"핵심, 단어, 쉼표구분\", \"UserPrompt\": \"사용자 지시 요약\", \"AgentResponse\": \"어떻게 처리했는지 요약\" }");
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

            // 5. Load Self-Evolved Skills from 'Skills' directory
            string skillsDir = Path.Combine(AppState.CurrentCwd, "Skills");
            if (Directory.Exists(skillsDir))
            {
                var skillFiles = Directory.GetFiles(skillsDir, "*.md");
                if (skillFiles.Length > 0)
                {
                    sb.AppendLine("## 🧠 Self-Evolved Skills (CRITICAL GUIDELINES)");
                    sb.AppendLine("아래는 당신의 과거 실패와 회고를 바탕으로 자율적으로 작성된 기술적 행동 지침입니다. 반드시 이 지침들을 최우선으로 준수하십시오.");
                    sb.AppendLine();
                    foreach (var file in skillFiles)
                    {
                        sb.AppendLine($"### [SKILL: {Path.GetFileName(file)}]");
                        sb.AppendLine(File.ReadAllText(file));
                        sb.AppendLine();
                    }
                }
            }
            
            return sb.ToString();
        }
    }
}
