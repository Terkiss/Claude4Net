using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SelfHealingService
    {
        private static readonly SelfHealingService _instance = new();
        public static SelfHealingService Instance => _instance;

        private readonly string _guidePath;

        private SelfHealingService()
        {
            _guidePath = Path.Combine(AppState.SystemBaseDir, "SELF_HEAL_GUIDE.md");
        }

        public string GetGuide()
        {
            if (File.Exists(_guidePath))
            {
                return File.ReadAllText(_guidePath);
            }
            return "# SELF_HEAL_GUIDE\nNo active self-healing guidelines found yet. Perform !reflect to generate insights.";
        }

        public void UpdateGuide(string reflectionSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SELF_HEAL_GUIDE");
            sb.AppendLine($"> Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## 🧠 Self-Reflection Analysis");
            sb.AppendLine(reflectionSummary);
            sb.AppendLine();
            sb.AppendLine("## 🚨 Execution Guardrails");
            sb.AppendLine("1. **Path Safety**: Always verify directory existence before writing files.");
            sb.AppendLine("2. **Build Integrity**: Run `dotnet build` after significant code changes.");
            sb.AppendLine("3. **Retry Strategy**: If a tool fails with a 'Permission' error, do NOT retry immediately. Check the path first.");
            sb.AppendLine("4. **Context Management**: If an error persists, use `!clear` or `reset` to refresh the agent context.");

            // Mask any sensitive info just in case
            string maskedContent = SourceGuard.MaskValue(sb.ToString());
            File.WriteAllText(_guidePath, maskedContent);
        }
    }
}
