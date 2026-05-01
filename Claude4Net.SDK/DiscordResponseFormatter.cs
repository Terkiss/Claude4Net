using System;
using System.Text;

namespace Claude4Net.SDK
{
    public static class DiscordResponseFormatter
    {
        public static string FormatStart(string user, string text)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🚀 **Task Started** for @{user}");
            sb.AppendLine($"> {Truncate(text, 100)}");
            return sb.ToString();
        }

        public static string FormatSuccess(string result, TimeSpan? duration = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("✅ **Task Completed**");
            sb.AppendLine("```");
            sb.AppendLine(Truncate(result, 1500));
            sb.AppendLine("```");
            if (duration.HasValue)
            {
                sb.AppendLine($"⏱️ **Duration**: {duration.Value.TotalSeconds:F1}s");
            }
            return sb.ToString();
        }

        public static string FormatError(string error)
        {
            return $"❌ **Task Failed**: {error}";
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }
    }
}
