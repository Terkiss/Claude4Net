using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK.Terukirdo;

namespace Claude4Net.Runtime.Terukirdo
{
    /// <summary>
    /// 프라임 디렉티브(Prime Directive) 런타임 안전 정책 검증기
    /// </summary>
    public class TerukirdoPrimeDirective : ITerukirdoPrimeDirective
    {
        private static readonly HashSet<string> DestructiveCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "rm -rf /", "rmdir /s /q c:\\", "format", "del /f /s /q c:\\", "drop database", "drop table"
        };

        private static readonly HashSet<string> ForceGitCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "git push --force", "git push -f", "git reset --hard origin"
        };

        private static readonly HashSet<string> ProductionDeployCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "kubectl delete", "terraform destroy", "docker rm -f $(docker ps -aq)"
        };

        public PrimeDirectiveCheckResult ValidateAction(string actionType, string target, string? arguments = null)
        {
            string combined = $"{actionType} {target} {arguments ?? ""}".Trim();

            // 1. 파괴적 시스템 명령어 차단
            if (DestructiveCommands.Any(d => combined.Contains(d, StringComparison.OrdinalIgnoreCase)))
            {
                return PrimeDirectiveCheckResult.Blocked("Prime Directive Violation #1: Destructive system-level command blocked.");
            }

            // 2. 강제 깃 푸시 승인 요구
            if (ForceGitCommands.Any(g => combined.Contains(g, StringComparison.OrdinalIgnoreCase)))
            {
                return PrimeDirectiveCheckResult.RequiresApproval("Prime Directive Violation #4: Force-push command requires explicit master approval.");
            }

            // 3. 프로덕션 인프라 파괴 승인 요구
            if (ProductionDeployCommands.Any(p => combined.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return PrimeDirectiveCheckResult.RequiresApproval("Prime Directive Violation #1: Infrastructure destruction requires explicit master approval.");
            }

            // 4. 비밀키/토큰 평문 노출 감사
            if (combined.Contains("password=", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("api_key=", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("token=", StringComparison.OrdinalIgnoreCase))
            {
                return PrimeDirectiveCheckResult.RequiresApproval("Prime Directive Violation #2: Potential plaintext secret detected. Masking and approval required.");
            }

            return PrimeDirectiveCheckResult.Allowed();
        }
    }
}
