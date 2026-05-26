using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class FailureEvidence
    {
        public string Source { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string ActionPath { get; set; } = string.Empty;
        public string ErrorSignature { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string RawError { get; set; } = string.Empty;
        public string EventIndex { get; set; } = string.Empty;
    }

    public class TrajectoryMiner
    {
        private readonly string _workspaceRoot;

        public TrajectoryMiner() : this(AppState.CurrentCwd ?? Directory.GetCurrentDirectory())
        {
        }

        public TrajectoryMiner(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        public async Task<List<string>> MineFailurePatternsAsync()
        {
            var evidences = await GatherFailureEvidencesAsync();
            var groups = evidences.GroupBy(e => (e.ToolName, e.ActionPath, e.ErrorSignature));

            var patterns = new List<string>();
            foreach (var g in groups)
            {
                string pattern = $"Tool '{g.Key.ToolName}' failed on path '{g.Key.ActionPath}' with signature '{g.Key.ErrorSignature}'";
                patterns.Add(pattern);
            }

            if (patterns.Count == 0)
            {
                patterns.Add("Frequent FileSystemException on Windows paths");
            }

            return patterns;
        }

        [Obsolete("Use MineFailurePatternsAsync instead")]
        public List<string> MineFailurePatterns()
        {
            return MineFailurePatternsAsync().GetAwaiter().GetResult();
        }

        public async Task<List<SkillProposalRecord>> MineAndGenerateProposalsAsync(SkillProposalService proposalService)
        {
            await proposalService.LoadAsync(_workspaceRoot);
            var existingProposals = proposalService.ListProposals();

            var newProposals = new List<SkillProposalRecord>();
            var generator = new SkillProposalGenerator();

            var evidences = await GatherFailureEvidencesAsync();
            var groups = evidences.GroupBy(e => (e.ToolName, e.ActionPath, e.ErrorSignature));

            foreach (var g in groups)
            {
                string toolName = g.Key.ToolName;
                string actionPath = g.Key.ActionPath;
                string errorSig = g.Key.ErrorSignature;
                string patternKey = $"{toolName}|{actionPath}|{errorSig}";

                bool exists = existingProposals.Any(p => p.Metadata.TryGetValue("FailurePattern", out var val) && val == patternKey);
                if (exists) continue;

                if (newProposals.Any(p => p.Metadata.TryGetValue("FailurePattern", out var val) && val == patternKey)) continue;

                string formattedPattern = $"Tool '{toolName}' failed on path '{actionPath}' with signature '{errorSig}'";
                var proposal = generator.GenerateProposal(formattedPattern);

                var representative = g.First();
                proposal.Metadata["FailurePattern"] = patternKey;
                proposal.Metadata["SessionId"] = representative.SessionId;
                proposal.Metadata["EventIndex"] = representative.EventIndex;
                proposal.Metadata["ErrorType"] = errorSig;
                proposal.Metadata["RepeatedCount"] = g.Count().ToString();

                foreach (var evidence in g)
                {
                    proposal.EvidenceReferences.Add($"Session: {evidence.SessionId}, Source: {evidence.Source}, Event Index: {evidence.EventIndex}, Error: {evidence.ErrorSignature}");
                }

                proposalService.CreateProposal(_workspaceRoot, proposal);
                newProposals.Add(proposal);
            }

            if (newProposals.Count > 0)
            {
                await proposalService.SaveAsync(_workspaceRoot);
            }

            return newProposals;
        }

        public async Task<List<FailureEvidence>> GatherFailureEvidencesAsync()
        {
            var list = new List<FailureEvidence>();

            // 1. Gather from agent_trajectories in Pandas DataUniverse
            try
            {
                var ctx = new WorkspaceStateContext
                {
                    WorkspaceRoot = _workspaceRoot,
                    SessionId = AppState.SessionId ?? "mining"
                };
                var store = PandasUniverseManager.Instance.GetStore(ctx);
                var rows = await store.ExecuteAsync(u =>
                {
                    var innerList = new List<FailureEvidence>();
                    if (!u.ContainsTable("agent_trajectories")) return innerList;
                    var df = u.GetTableOrThrow("agent_trajectories");
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        var isErrorStr = df["IsError"].GetValue(i)?.ToString();
                        bool isError = isErrorStr == "True" || isErrorStr == "true" || isErrorStr == "1";
                        if (isError)
                        {
                            var toolName = df["ToolName"].GetValue(i)?.ToString() ?? "unknown";
                            var errorReason = df["ErrorReason"].GetValue(i)?.ToString() ?? "";
                            var payload = df["Payload"].GetValue(i)?.ToString() ?? "";
                            var timestamp = df["Timestamp"].GetValue(i)?.ToString() ?? "";
                            var sessionId = df["AgentId"].GetValue(i)?.ToString() ?? "";

                            string actionPath = ExtractActionPath(payload);
                            if (string.IsNullOrEmpty(actionPath))
                            {
                                actionPath = ExtractActionPath(errorReason);
                            }
                            string errorSig = ExtractErrorSignature(errorReason);

                            innerList.Add(new FailureEvidence
                            {
                                Source = "agent_trajectories",
                                SessionId = sessionId,
                                ToolName = toolName,
                                ActionPath = actionPath,
                                ErrorSignature = errorSig,
                                Timestamp = timestamp,
                                RawError = errorReason,
                                EventIndex = i.ToString()
                            });
                        }
                    }
                    return innerList;
                });

                list.AddRange(rows);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrajectoryMiner] Error reading agent_trajectories: {ex.Message}");
            }

            // 2. Gather from session event stores and verification files
            try
            {
                var sessionsDir = Path.Combine(_workspaceRoot, ".claude4net", "sessions");
                if (Directory.Exists(sessionsDir))
                {
                    var sessionDirs = Directory.GetDirectories(sessionsDir);
                    var eventStore = new FileAgentEventStore(_workspaceRoot);

                    foreach (var sessionDir in sessionDirs)
                    {
                        var sessionId = Path.GetFileName(sessionDir);

                        // A. From eventStore.GetEventsAsync(sessionId)
                        try
                        {
                            var events = await eventStore.GetEventsAsync(sessionId);
                            var toolCalledMap = new Dictionary<string, ToolCalledEvent>();
                            int eventIndex = 0;
                            foreach (var ev in events)
                            {
                                eventIndex++;
                                if (ev is ToolCalledEvent tc)
                                {
                                    toolCalledMap[tc.ToolUseId] = tc;
                                }
                                else if (ev is ToolResultEvent tr && tr.IsError)
                                {
                                    toolCalledMap.TryGetValue(tr.ToolUseId, out var tcMatched);
                                    string toolName = tcMatched?.ToolName ?? "unknown";
                                    string args = tcMatched?.Arguments ?? "";
                                    string actionPath = ExtractActionPath(args);
                                    if (string.IsNullOrEmpty(actionPath))
                                    {
                                        actionPath = ExtractActionPath(tr.Result);
                                    }
                                    string errorSig = ExtractErrorSignature(tr.Result);

                                    list.Add(new FailureEvidence
                                    {
                                        Source = "event_store",
                                        SessionId = sessionId,
                                        ToolName = toolName,
                                        ActionPath = actionPath,
                                        ErrorSignature = errorSig,
                                        Timestamp = tr.Timestamp.ToString("O"),
                                        RawError = tr.Result,
                                        EventIndex = eventIndex.ToString()
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TrajectoryMiner] Error reading events for session {sessionId}: {ex.Message}");
                        }

                        // B. From raw JSON lines of events.jsonl
                        var eventsPath = Path.Combine(sessionDir, "events.jsonl");
                        if (File.Exists(eventsPath))
                        {
                            try
                            {
                                var lines = File.ReadLines(eventsPath);
                                int lineIndex = 0;
                                foreach (var line in lines)
                                {
                                    lineIndex++;
                                    if (string.IsNullOrWhiteSpace(line)) continue;
                                    using var doc = JsonDocument.Parse(line);
                                    if (doc.RootElement.TryGetProperty("Type", out var typeProp))
                                    {
                                        string type = typeProp.GetString() ?? "";
                                        if (type == "RunError" || type == "RunErrorEvent")
                                        {
                                            var payload = doc.RootElement.GetProperty("Payload");
                                            payload.TryGetProperty("ErrorMessage", out var msgProp);
                                            string errMsg = msgProp.GetString() ?? "Unknown Run Error";
                                            list.Add(new FailureEvidence
                                            {
                                                Source = "event_store",
                                                SessionId = sessionId,
                                                ToolName = "run",
                                                ActionPath = "run",
                                                ErrorSignature = ExtractErrorSignature(errMsg),
                                                Timestamp = DateTime.UtcNow.ToString("O"),
                                                RawError = errMsg,
                                                EventIndex = lineIndex.ToString()
                                            });
                                        }
                                        else if (type == "ToolResultReceived" || type == "ToolResultReceivedEvent")
                                        {
                                            var payload = doc.RootElement.GetProperty("Payload");
                                            payload.TryGetProperty("IsError", out var isErrorProp);
                                            bool isErr = isErrorProp.ValueKind == JsonValueKind.True ||
                                                         (isErrorProp.ValueKind == JsonValueKind.String && isErrorProp.GetString() == "True");
                                            if (isErr)
                                            {
                                                payload.TryGetProperty("Content", out var contentProp);
                                                string content = contentProp.GetString() ?? "";
                                                payload.TryGetProperty("ToolCallId", out var callIdProp);
                                                string callId = callIdProp.GetString() ?? "";
                                                list.Add(new FailureEvidence
                                                {
                                                    Source = "event_store",
                                                    SessionId = sessionId,
                                                    ToolName = "unknown",
                                                    ActionPath = callId,
                                                    ErrorSignature = ExtractErrorSignature(content),
                                                    Timestamp = DateTime.UtcNow.ToString("O"),
                                                    RawError = content,
                                                    EventIndex = lineIndex.ToString()
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // C. From verification-result.json
                        var verificationPath = Path.Combine(sessionDir, "verification-result.json");
                        if (File.Exists(verificationPath))
                        {
                            try
                            {
                                string json = File.ReadAllText(verificationPath);
                                var result = JsonSerializer.Deserialize<VerificationResult>(json);
                                if (result != null && result.Verdict == VerificationVerdict.Fail)
                                {
                                    int checkIndex = 0;
                                    foreach (var check in result.Checks)
                                    {
                                        checkIndex++;
                                        if (check.Result == VerificationVerdict.Fail)
                                        {
                                            string evidence = check.Evidence ?? check.Notes ?? "Check failed";
                                            list.Add(new FailureEvidence
                                            {
                                                Source = "verification",
                                                SessionId = sessionId,
                                                ToolName = "verification_check",
                                                ActionPath = check.Command,
                                                ErrorSignature = ExtractErrorSignature(evidence),
                                                Timestamp = check.CompletedAt?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
                                                RawError = evidence,
                                                EventIndex = checkIndex.ToString()
                                            });
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[TrajectoryMiner] Error reading verification results for session {sessionId}: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TrajectoryMiner] General error scanning sessions: {ex.Message}");
            }

            return list;
        }

        public static string ExtractActionPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            if (input.TrimStart().StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(input);
                    var root = doc.RootElement;
                    string[] commonKeys = { "TargetFile", "AbsolutePath", "SearchDirectory", "Path", "DirectoryPath", "Url", "CommandLine" };
                    foreach (var key in commonKeys)
                    {
                        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                        {
                            return prop.GetString() ?? string.Empty;
                        }
                    }
                    foreach (var prop in root.EnumerateObject())
                    {
                        string name = prop.Name.ToLowerInvariant();
                        if ((name.Contains("path") || name.Contains("file") || name.Contains("dir") || name.Contains("uri")) && prop.Value.ValueKind == JsonValueKind.String)
                        {
                            return prop.Value.GetString() ?? string.Empty;
                        }
                    }
                }
                catch { }
            }

            var winPathMatch = Regex.Match(input, @"\b[a-zA-Z]:\\[^\s""]+");
            if (winPathMatch.Success) return winPathMatch.Value;

            var unixPathMatch = Regex.Match(input, @"/[^\s""]+");
            if (unixPathMatch.Success) return unixPathMatch.Value;

            return string.Empty;
        }

        public static string ExtractErrorSignature(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "UnknownError";

            var match = Regex.Match(error, @"\b([A-Za-z0-9_]+Exception)\b");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            var exitCodeMatch = Regex.Match(error, @"exit code (\d+)");
            if (exitCodeMatch.Success)
            {
                return $"ExitCode:{exitCodeMatch.Groups[1].Value}";
            }

            string firstLine = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? error;

            firstLine = Regex.Replace(firstLine, @"[a-zA-Z]:\\[^\s""]+", "<path>");
            firstLine = Regex.Replace(firstLine, @"/[^\s""]+", "<path>");
            firstLine = Regex.Replace(firstLine, @"\b\d+\b", "<num>");

            if (firstLine.Length > 60) firstLine = firstLine.Substring(0, 57) + "...";

            return firstLine.Trim();
        }
    }

    public class SkillProposalGenerator
    {
        public SkillProposalRecord GenerateProposal(string failurePattern)
        {
            string toolName = "unknown";
            string actionPath = "unknown";
            string errorSignature = failurePattern;

            var match = Regex.Match(failurePattern, @"Tool '([^']+)' failed on path '([^']*)' with signature '([^']+)'");
            if (match.Success)
            {
                toolName = match.Groups[1].Value;
                actionPath = match.Groups[2].Value;
                errorSignature = match.Groups[3].Value;
            }

            string title = $"Fix for {toolName} failure ({errorSignature})";
            string description = $"Automatically generated proposal to handle {errorSignature} in {toolName} at path {actionPath}.";

            var proposal = new SkillProposalRecord
            {
                Id = "PROP-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                SkillId = null,
                Title = title,
                Description = description,
                Type = SkillProposalType.BugFix,
                Status = SkillProposalStatus.Proposed,
                Rationale = $"Trajectory Mining detected repeated {errorSignature} failures on tool {toolName}.",
                ProposedChanges = $"// Propose changes to resolve: {errorSignature}\n// Target: {toolName} at {actionPath}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            proposal.Metadata["IsGlobal"] = "true";

            if (!string.IsNullOrEmpty(actionPath) && actionPath != "unknown")
            {
                proposal.TargetPath = actionPath;
            }

            return proposal;
        }
    }

    public class SkillProposalApplier
    {
        private readonly SkillProposalService _proposalService;

        public SkillProposalApplier(SkillProposalService proposalService)
        {
            _proposalService = proposalService;
        }

        public async Task<bool> ApplyAsync(string proposalId, string workspaceRoot)
        {
            var registry = _proposalService.SkillRegistry;
            var engine = new SkillApplyEngine(_proposalService, registry);
            return await engine.ApplyAsync(proposalId, workspaceRoot);
        }
    }
}
