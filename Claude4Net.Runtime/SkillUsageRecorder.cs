using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SkillUsageRecorder
    {
        private readonly IServiceProvider? _serviceProvider;
        private static readonly object _fileLock = new();

        public string? WorkspaceRoot { get; set; }

        public SkillUsageRecorder() : this(null)
        {
        }

        public SkillUsageRecorder(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public class SkillUsageRecord
        {
            public string SkillId { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
            public bool Success { get; set; }
            public long ElapsedMs { get; set; }
            public int Score { get; set; }
            public string? Error { get; set; }
        }

        public void Record(string skillId, bool success, int score)
        {
            RecordAsync(skillId, success, 0, score).GetAwaiter().GetResult();
        }

        public async Task RecordAsync(string skillId, bool success, long elapsedMs, int score, string? error = null)
        {
            // 1. Get workspace root
            string ws = WorkspaceRoot ?? AppState.CurrentCwd ?? AppState.SystemBaseDir;
            if (string.IsNullOrEmpty(ws))
            {
                ws = Directory.GetCurrentDirectory();
            }

            // Update SkillRegistry metrics if possible
            var registry = _serviceProvider?.GetService<SkillRegistryService>();
            if (registry != null)
            {
                registry.UpdateMetrics(skillId, success, score);
                await registry.SaveAsync();
            }

            // 2. Log to .claude4net/skill-usage.jsonl
            var usageRecord = new SkillUsageRecord
            {
                SkillId = skillId,
                Timestamp = DateTime.UtcNow,
                Success = success,
                ElapsedMs = elapsedMs,
                Score = score,
                Error = error
            };

            string baseDir = Path.Combine(ws, ".claude4net");
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            string usageFile = Path.Combine(baseDir, "skill-usage.jsonl");
            string line = JsonSerializer.Serialize(usageRecord, new JsonSerializerOptions { WriteIndented = false }) + Environment.NewLine;

            lock (_fileLock)
            {
                File.AppendAllText(usageFile, line);
            }

            // 3. Trigger auto proposals if failure thresholds are met
            await CheckThresholdAndTriggerAsync(ws, skillId);
        }

        private async Task CheckThresholdAndTriggerAsync(string workspaceRoot, string skillId)
        {
            // Read usage records to compute statistics
            string baseDir = Path.Combine(workspaceRoot, ".claude4net");
            string usageFile = Path.Combine(baseDir, "skill-usage.jsonl");
            if (!File.Exists(usageFile)) return;

            int failureCount = 0;
            int totalCount = 0;

            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(usageFile))
                    {
                        var lines = File.ReadAllLines(usageFile);
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            var record = JsonSerializer.Deserialize<SkillUsageRecord>(line);
                            if (record != null && record.SkillId.Equals(skillId, StringComparison.OrdinalIgnoreCase))
                            {
                                totalCount++;
                                if (!record.Success)
                                {
                                    failureCount++;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore errors reading file
                }
            }

            // Threshold: total attempts >= 3 and failureCount >= 3 or failureRate >= 50%
            bool thresholdReached = false;
            if (totalCount >= 3)
            {
                double failureRate = (double)failureCount / totalCount;
                if (failureCount >= 3 || failureRate >= 0.5)
                {
                    thresholdReached = true;
                }
            }

            if (thresholdReached)
            {
                var proposalService = _serviceProvider?.GetService<SkillProposalService>();
                if (proposalService != null)
                {
                    // Trigger SelfEvolvingSkills to mine and generate proposals
                    var miner = new TrajectoryMiner(workspaceRoot);
                    await miner.MineAndGenerateProposalsAsync(proposalService);
                }
            }
        }
    }
}
