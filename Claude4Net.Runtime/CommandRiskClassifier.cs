using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Claude4Net.Runtime
{
    public enum CommandRiskLevel
    {
        Safe,
        NeedsApproval,
        Dangerous
    }

    public sealed record CommandRiskAssessment(CommandRiskLevel Level, string Reason, IReadOnlyList<string> MatchedPatterns)
    {
        public bool RequiresApproval => Level != CommandRiskLevel.Safe;
    }

    public sealed class CommandRiskClassifier
    {
        private static readonly (Regex Pattern, CommandRiskLevel Level, string Reason)[] Rules =
        {
            (new Regex(@"(^|\s)(rm\s+(-[^\s]*[rf][^\s]*|-[^\s]*[fr][^\s]*)|del\s+/[fqs]|rmdir\s+/s|Remove-Item\b.*\s-(Recurse|Force))", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.Dangerous, "recursive or forced delete"),
            (new Regex(@"(^|\s)(sudo|su|runas)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.Dangerous, "privilege escalation"),
            (new Regex(@"(^|\s)(format|mkfs|diskpart|dd)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.Dangerous, "disk or filesystem mutation"),
            (new Regex(@"(^|\s)(chmod|chown|icacls|takeown)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.NeedsApproval, "permission mutation"),
            (new Regex(@"(^|\s)(curl|wget|Invoke-WebRequest|iwr|Invoke-RestMethod)\b.*(\||>\s*|;\s*(sh|bash|powershell|pwsh)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.Dangerous, "download and execute pattern"),
            (new Regex(@"(^|\s)(git\s+reset\s+--hard|git\s+clean\s+-[^\s]*[fdx])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.Dangerous, "destructive git operation"),
            (new Regex(@"(^|\s)(powershell|pwsh|bash|sh|cmd)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), CommandRiskLevel.NeedsApproval, "nested shell execution")
        };

        public CommandRiskAssessment Classify(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new CommandRiskAssessment(CommandRiskLevel.Safe, "empty command", Array.Empty<string>());
            }

            var matches = new List<string>();
            var level = CommandRiskLevel.Safe;
            var reason = "no risky command pattern detected";

            foreach (var (pattern, ruleLevel, ruleReason) in Rules)
            {
                if (!pattern.IsMatch(command)) continue;

                matches.Add(pattern.ToString());
                if (ruleLevel > level)
                {
                    level = ruleLevel;
                    reason = ruleReason;
                }
            }

            return new CommandRiskAssessment(level, reason, matches);
        }

        public CommandRiskAssessment ClassifyFromToolInput(string toolName, object? input)
        {
            if (!toolName.Contains("bash", StringComparison.OrdinalIgnoreCase) &&
                !toolName.Contains("shell", StringComparison.OrdinalIgnoreCase) &&
                !toolName.Equals("sh", StringComparison.OrdinalIgnoreCase))
            {
                return new CommandRiskAssessment(CommandRiskLevel.Safe, "not a shell tool", Array.Empty<string>());
            }

            var command = ExtractCommand(input);
            return Classify(command);
        }

        private static string? ExtractCommand(object? input)
        {
            if (input == null) return null;

            if (input is System.Text.Json.JsonElement element)
            {
                return element.TryGetProperty("command", out var commandElement)
                    ? commandElement.GetString()
                    : null;
            }

            var property = input.GetType().GetProperties()
                .FirstOrDefault(p => p.Name.Equals("command", StringComparison.OrdinalIgnoreCase));
            return property?.GetValue(input)?.ToString();
        }
    }
}
