using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime.Services
{
    public class ToolSecurityService
    {
        public bool IsSensitiveTool(string name)
        {
            var sensitivePrefixes = new[] { "bash", "write", "edit", "delete", "shell", "sh", "sensitive" };
            return sensitivePrefixes.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsFileModifyingTool(string name)
        {
            var modifiers = new[] { "write", "edit", "replace", "sed", "patch", "delete", "remove", "save" };
            return modifiers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsMemoryModifyingTool(string name)
        {
            var memoryModifiers = new[] { "pandas_agent_memory_upsert", "pandas_agent_memory_clear", "pandas_restore", "pandas_import" };
            return memoryModifiers.Any(m => name.Equals(m, StringComparison.OrdinalIgnoreCase));
        }

        public async Task LogAuditAsync(string toolName, string input, PathSafetyResult safety, bool? approved, string status)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("audit_logs")) return null!;
                var df = u.GetTableOrThrow("audit_logs");
                var maskedInput = SourceGuard.Filter(input).FilteredText;

                var newRowCols = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }),
                    ["User"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { Environment.UserName }),
                    ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { toolName }),
                    ["Input"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { maskedInput }),
                    ["SafetyResult"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { safety.ToString() }),
                    ["Approved"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { approved?.ToString() ?? "N/A" }),
                    ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { status })
                };

                var newRowDf = new DataFrame(newRowCols);
                var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                u.AddOrUpdateTable("audit_logs", updatedDf);
                return null!;
            });
        }
    }
}
