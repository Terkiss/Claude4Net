using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Claude4Net.Commands.Handlers
{
    public static class SpecVerifyCommands
    {
        public static async Task<string> HandleSpec(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd))
                return "[red]Error:[/] Workspace not set. Use /setworkspace <path> first.";

            var store = new SeedSpecStore(AppState.CurrentCwd);
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "[bold cyan]Usage:[/]\n" +
                       "  /spec list\n" +
                       "  /spec new <id> <title>\n" +
                       "  /spec show <id>\n" +
                       "  /spec question <id> <question>\n" +
                       "  /spec answer <id> <questionId> <answer>\n" +
                       "  /spec criteria add <id> <description>\n" +
                       "  /spec lock <id>\n" +
                       "  /spec attach <specId> <coordinateTaskId>";
            }

            string sub = parts[0].ToLowerInvariant();

            bool IsValidSpecId(string specId)
            {
                if (string.IsNullOrEmpty(specId)) return false;
                if (specId.Contains("..") || specId.Contains('/') || specId.Contains('\\') || specId.Contains(':')) return false;
                var invalidChars = Path.GetInvalidFileNameChars();
                if (specId.Any(c => invalidChars.Contains(c))) return false;
                return true;
            }

            switch (sub)
            {
                case "list":
                    {
                        var specs = store.ListSpecs().ToList();
                        if (!specs.Any()) return "[grey]No specs found in workspace.[/]";

                        var table = new Table().Border(TableBorder.Rounded);
                        table.AddColumn("[bold]ID[/]");
                        table.AddColumn("[bold]Title[/]");
                        table.AddColumn("[bold]Status[/]");
                        table.AddColumn("[bold]Criteria[/]");
                        table.AddColumn("[bold]Open Qs[/]");
                        table.AddColumn("[bold]Updated At[/]");

                        foreach (var s in specs.OrderBy(x => x.Id))
                        {
                            string statusColor = s.Status switch
                            {
                                SeedSpecStatus.Locked => "green",
                                SeedSpecStatus.Draft => "yellow",
                                SeedSpecStatus.NeedsClarification => "blue",
                                SeedSpecStatus.Superseded => "grey",
                                _ => "white"
                            };
                            table.AddRow(
                                Markup.Escape(s.Id),
                                Markup.Escape(s.Title),
                                $"[{statusColor}]{s.Status}[/]",
                                s.AcceptanceCriteria.Count.ToString(),
                                s.OpenQuestions.Count.ToString(),
                                s.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                            );
                        }

                        AnsiConsole.Write(table);
                        return $"Total {specs.Count} specs listed.";
                    }

                case "new":
                    {
                        var newParts = a.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                        if (newParts.Length < 3)
                        {
                            return "[red]Error:[/] Usage: /spec new <id> <title>";
                        }

                        string specId = newParts[1];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var existing = await store.LoadAsync(specId);
                        if (existing != null)
                        {
                            return $"[red]Error:[/] Spec with ID '{specId}' already exists.";
                        }

                        string title = newParts[2].Trim('\"');
                        var spec = new SeedSpecRecord
                        {
                            Id = specId,
                            Title = title,
                            Status = SeedSpecStatus.Draft,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };
                        await store.SaveAsync(spec);
                        return $"[green]Spec '{title}' created successfully with ID '{specId}'.[/]";
                    }

                case "show":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /spec show <id>";
                        }

                        string specId = parts[1];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[bold cyan]Spec:[/] {Markup.Escape(spec.Title)} ([bold]{Markup.Escape(spec.Id)}[/])");
                        sb.AppendLine($"[bold]Status:[/] {spec.Status}");
                        sb.AppendLine($"[bold]Goal:[/] {Markup.Escape(string.IsNullOrEmpty(spec.Goal) ? "(None)" : spec.Goal)}");
                        sb.AppendLine();
                        sb.AppendLine("[bold yellow]Acceptance Criteria:[/]");
                        if (spec.AcceptanceCriteria.Count == 0)
                        {
                            sb.AppendLine("  (No acceptance criteria defined)");
                        }
                        else
                        {
                            foreach (var ac in spec.AcceptanceCriteria)
                            {
                                string reqStr = ac.Required ? "[red](Required)[/]" : "[grey](Optional)[/]";
                                sb.AppendLine($"  - [[[bold]{Markup.Escape(ac.Id)}[/]]] {Markup.Escape(ac.Description)} {reqStr}");
                            }
                        }
                        sb.AppendLine();
                        sb.AppendLine("[bold yellow]Open Questions:[/]");
                        if (spec.OpenQuestions.Count == 0)
                        {
                            sb.AppendLine("  (No open questions)");
                        }
                        else
                        {
                            foreach (var q in spec.OpenQuestions)
                            {
                                string blockStr = q.IsBlocking ? "[red](Blocking)[/]" : "[grey](Non-blocking)[/]";
                                string ansStr = string.IsNullOrEmpty(q.Answer) ? "[yellow]Unanswered[/]" : $"[green]Answered:[/] {Markup.Escape(q.Answer)}";
                                sb.AppendLine($"  - [[[bold]{Markup.Escape(q.Id)}[/]]] {Markup.Escape(q.Question)} {blockStr}");
                                sb.AppendLine($"    {ansStr}");
                            }
                        }
                        return sb.ToString();
                    }

                case "question":
                    {
                        var qParts = a.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                        if (qParts.Length < 3)
                        {
                            return "[red]Error:[/] Usage: /spec question <id> <question>";
                        }

                        string specId = qParts[1];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        string questionText = qParts[2].Trim('\"');

                        int nextNum = 1;
                        if (spec.OpenQuestions.Any())
                        {
                            var maxId = spec.OpenQuestions
                                .Select(q => q.Id)
                                .Where(id => id.StartsWith("Q-") && int.TryParse(id.Substring(2), out _))
                                .Select(id => int.Parse(id.Substring(2)))
                                .DefaultIfEmpty(0)
                                .Max();
                            nextNum = maxId + 1;
                        }
                        string qId = $"Q-{nextNum}";

                        var clarifyingQuestion = new ClarifyingQuestion
                        {
                            Id = qId,
                            Question = questionText,
                            IsBlocking = true
                        };
                        spec.OpenQuestions.Add(clarifyingQuestion);
                        await store.SaveAsync(spec);
                        return $"[green]Blocking question '{qId}' added to Spec '{specId}':[/] {Markup.Escape(questionText)}";
                    }

                case "answer":
                    {
                        var ansParts = a.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                        if (ansParts.Length < 4)
                        {
                            return "[red]Error:[/] Usage: /spec answer <id> <questionId> <answer>";
                        }

                        string specId = ansParts[1];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        string qId = ansParts[2];
                        var question = spec.OpenQuestions.FirstOrDefault(q => q.Id.Equals(qId, StringComparison.OrdinalIgnoreCase));
                        if (question == null)
                        {
                            return $"[red]Error:[/] Question '{qId}' not found in Spec '{specId}'.";
                        }

                        string answerText = ansParts[3].Trim('\"');
                        question.Answer = answerText;
                        await store.SaveAsync(spec);
                        return $"[green]Question '{qId}' in Spec '{specId}' answered successfully.[/]";
                    }

                case "criteria":
                    {
                        var critParts = a.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                        if (critParts.Length < 4 || !critParts[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                        {
                            return "[red]Error:[/] Usage: /spec criteria add <id> <description>";
                        }

                        string specId = critParts[2];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        string descText = critParts[3].Trim('\"');

                        int nextNum = 1;
                        if (spec.AcceptanceCriteria.Any())
                        {
                            var maxId = spec.AcceptanceCriteria
                                .Select(ac => ac.Id)
                                .Where(id => id.StartsWith("AC-") && int.TryParse(id.Substring(3), out _))
                                .Select(id => int.Parse(id.Substring(3)))
                                .DefaultIfEmpty(0)
                                .Max();
                            nextNum = maxId + 1;
                        }
                        string acId = $"AC-{nextNum}";

                        var criterion = new AcceptanceCriterion
                        {
                            Id = acId,
                            Description = descText,
                            Required = true
                        };
                        spec.AcceptanceCriteria.Add(criterion);
                        await store.SaveAsync(spec);
                        return $"[green]Required acceptance criterion '{acId}' added to Spec '{specId}':[/] {Markup.Escape(descText)}";
                    }

                case "lock":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /spec lock <id>";
                        }

                        string specId = parts[1];
                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        var unansweredBlocking = spec.OpenQuestions.Where(q => q.IsBlocking && string.IsNullOrEmpty(q.Answer)).ToList();
                        if (unansweredBlocking.Any())
                        {
                            return $"[red]Error: Cannot lock spec. Unanswered blocking questions exist: {string.Join(", ", unansweredBlocking.Select(q => q.Id))}.[/]";
                        }

                        var requiredCriteria = spec.AcceptanceCriteria.Where(ac => ac.Required).ToList();
                        if (!requiredCriteria.Any())
                        {
                            return "[red]Error: Cannot lock spec. At least one required acceptance criterion is needed.[/]";
                        }

                        spec.Status = SeedSpecStatus.Locked;
                        await store.SaveAsync(spec);
                        return $"[green]Spec '{specId}' is now Locked.[/]";
                    }

                case "attach":
                    {
                        if (parts.Length < 3)
                        {
                            return "[red]Error:[/] Usage: /spec attach <specId> <coordinateTaskId>";
                        }

                        string specId = parts[1];
                        string coordinateTaskId = parts[2];

                        if (!IsValidSpecId(specId))
                        {
                            return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                        }

                        var spec = await store.LoadAsync(specId);
                        if (spec == null)
                        {
                            return $"[red]Error:[/] Spec '{specId}' not found.";
                        }

                        if (spec.Status != SeedSpecStatus.Locked)
                        {
                            return $"[red]Error: Spec '{specId}' is not in Locked status (Current: {spec.Status}). Only Locked specs can be attached.[/]";
                        }

                        if (!AppState.Tasks.TryGetValue(coordinateTaskId, out var st) || st is not CoordinateTask task)
                        {
                            return $"[red]Error: Coordinate task '{coordinateTaskId}' not found.[/]";
                        }

                        CoordinatorStore.Instance.SyncGatesFromSpec(coordinateTaskId, spec);

                        var generatedGates = spec.AcceptanceCriteria.Select(ac => "Spec-" + ac.Id).ToList();
                        return $"[green]Spec '{specId}' successfully attached to Coordinate Task '{coordinateTaskId}'.[/]\nGenerated gates: {string.Join(", ", generatedGates)}";
                    }

                default:
                    return $"[red]Error:[/] Unknown subcommand '{sub}'.";
            }
        }

        public static async Task<string> HandleVerify(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return "[red]Error:[/] Workspace not set.";

            var orchestrator = new VerificationOrchestrator(AppState.CurrentCwd);
            var session = orchestrator.CreateVerifierSession(AppState.SessionId);
            var checks = new List<VerificationCheck>();

            // 검증 세션은 읽기 전용입니다.
            try
            {
                VerificationOrchestrator.EnforceReadOnly(session, "test-write");
                // 위 호출이 예외를 던져야 정상입니다.
            }
            catch (System.Security.SecurityException)
            {
                // 예상되는 정상 동작 - 읽기 전용 정책이 작동 중
            }

            // Check 1: Standard Build
            try
            {
                var buildPsi = new ProcessStartInfo("dotnet", "build -p:UseAppHost=false")
                {
                    WorkingDirectory = AppState.CurrentCwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var buildProcess = Process.Start(buildPsi);
                string buildOutput = buildProcess != null ? await buildProcess.StandardOutput.ReadToEndAsync() : "";
                string buildError = buildProcess != null ? await buildProcess.StandardError.ReadToEndAsync() : "";
                buildProcess?.WaitForExit();
                int? buildExit = buildProcess?.ExitCode;

                checks.Add(orchestrator.RunCheck("Standard Build", "dotnet build -p:UseAppHost=false",
                    buildOutput + buildError, buildExit));
            }
            catch
            {
                checks.Add(orchestrator.RunCheck("Standard Build", "dotnet build -p:UseAppHost=false", null, null));
            }

            // Check 2: Strict Nullable Build
            try
            {
                var strictPsi = new ProcessStartInfo("dotnet", "build -p:UseAppHost=false -p:TreatWarningsAsErrors=true")
                {
                    WorkingDirectory = AppState.CurrentCwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var strictProcess = Process.Start(strictPsi);
                string strictOutput = strictProcess != null ? await strictProcess.StandardOutput.ReadToEndAsync() : "";
                string strictError = strictProcess != null ? await strictProcess.StandardError.ReadToEndAsync() : "";
                strictProcess?.WaitForExit();
                int? strictExit = strictProcess?.ExitCode;

                checks.Add(orchestrator.RunCheck("Strict Nullable Build", "dotnet build -p:UseAppHost=false -p:TreatWarningsAsErrors=true",
                    strictOutput + strictError, strictExit));
            }
            catch
            {
                checks.Add(orchestrator.RunCheck("Strict Nullable Build", "dotnet build -p:UseAppHost=false -p:TreatWarningsAsErrors=true", null, null));
            }

            // Check 3: Unit Tests
            try
            {
                var testPsi = new ProcessStartInfo("dotnet", "test --no-build")
                {
                    WorkingDirectory = AppState.CurrentCwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var testProcess = Process.Start(testPsi);
                string testOutput = testProcess != null ? await testProcess.StandardOutput.ReadToEndAsync() : "";
                string testError = testProcess != null ? await testProcess.StandardError.ReadToEndAsync() : "";
                testProcess?.WaitForExit();
                int? testExit = testProcess?.ExitCode;

                checks.Add(orchestrator.RunCheck("Unit Tests", "dotnet test --no-build",
                    testOutput + testError, testExit));
            }
            catch
            {
                checks.Add(orchestrator.RunCheck("Unit Tests", "dotnet test --no-build", null, null));
            }

            // 결과 집계 및 저장
            var result = orchestrator.AggregateResult(session.VerifierSessionId, session.GeneratorSessionId, checks);
            await orchestrator.WriteResultAsync(result);

            // CLI 출력 포맷
            string cliOutput = VerificationOrchestrator.FormatResultForCli(result);
            return cliOutput;
        }

        public static async Task<string> HandleSkills(string a, IServiceProvider sp)
        {
            var registry = sp.GetService<SkillRegistryService>();
            if (registry == null) return "[red]Error:[/] SkillRegistryService not available.";

            await registry.LoadAsync();
            var skills = registry.ListSkills();

            if (!skills.Any()) return "[grey]No skills registered in the current registry.[/]";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]Discovered Skills:[/]");

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]ID[/]");
            table.AddColumn("[bold]Display Name[/]");
            table.AddColumn("[bold]Version[/]");
            table.AddColumn("[bold]Success/Fail[/]");
            table.AddColumn("[bold]Avg Score[/]");

            foreach(var s in skills.OrderBy(x => x.Id))
            {
                string scoreColor = s.Metrics.AverageScore > 0.8 ? "green" : s.Metrics.AverageScore > 0.5 ? "yellow" : "red";
                table.AddRow(
                    Markup.Escape(s.Id),
                    Markup.Escape(s.DisplayName),
                    Markup.Escape(s.Version),
                    $"{s.Metrics.SuccessCount}/{s.Metrics.FailureCount}",
                    $"[{scoreColor}]{s.Metrics.AverageScore:P0}[/]"
                );
            }

            AnsiConsole.Write(table);
            return $"Total {skills.Count} skills listed.";
        }

        public static async Task<string> HandleSkillProposals(string a, IServiceProvider sp)
        {
            var proposalService = sp.GetService<SkillProposalService>();
            if (proposalService == null) return "[red]Error:[/] SkillProposalService not available.";

            string? ws = AppState.CurrentCwd;
            if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";

            await proposalService.LoadAsync(ws);
            var proposals = proposalService.ListProposals();

            if (!proposals.Any()) return "[grey]No skill proposals found.[/]";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]Skill Evolution Proposals:[/]");

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold]ID[/]");
            table.AddColumn("[bold]Target[/]");
            table.AddColumn("[bold]Type[/]");
            table.AddColumn("[bold]Summary[/]");
            table.AddColumn("[bold]Status[/]");

            foreach(var p in proposals.OrderByDescending(x => x.CreatedAt))
            {
                string statusColor = p.Status switch {
                    SkillProposalStatus.Approved => "green",
                    SkillProposalStatus.Rejected => "red",
                    SkillProposalStatus.Proposed => "yellow",
                    _ => "grey"
                };
                string target = p.SkillId ?? (p.TargetPath != null ? Path.GetFileName(p.TargetPath) : "New");
                table.AddRow(
                    Markup.Escape(p.Id),
                    Markup.Escape(target),
                    Markup.Escape(p.Type.ToString()),
                    Markup.Escape(p.Title),
                    $"[{statusColor}]{p.Status}[/]"
                );
            }

            AnsiConsole.Write(table);
            return $"Total {proposals.Count} proposals listed. Approving a proposal does not apply file changes.";
        }

        public static async Task<string> HandleSkillPropose(string a, IServiceProvider sp)
        {
            var proposalService = sp.GetService<SkillProposalService>();
            if (proposalService == null) return "[red]Error:[/] SkillProposalService not available.";

            string? ws = AppState.CurrentCwd;
            if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";

            var parts = a.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "Usage: !skill-propose <skillId_or_path> <summary>";

            string target = parts[0];
            string summary = parts[1];

            var proposal = new SkillProposalRecord {
                Title = summary,
                Status = SkillProposalStatus.Proposed
            };

            if (target.Contains("/") || target.Contains("\\") || target.EndsWith(".md"))
                proposal.TargetPath = target;
            else
                proposal.SkillId = target;

            try {
                await proposalService.LoadAsync(ws);
                proposalService.CreateProposal(ws, proposal);
                await proposalService.SaveAsync(ws);
                return $"[green]Proposal '{proposal.Id}' created successfully.[/] Use !skill-proposals to view status.";
            } catch (Exception ex) {
                return $"[red]Error creating proposal:[/] {ex.Message}";
            }
        }

        public static async Task<string> HandleSkill(string a, IServiceProvider sp)
        {
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "Usage: /skill <analyze|proposals|propose|validate|approve|reject|apply>";
            }

            string sub = parts[0].ToLowerInvariant();
            var proposalService = sp.GetService<SkillProposalService>();
            if (proposalService == null) return "[red]Error:[/] SkillProposalService not available.";

            string? ws = AppState.CurrentCwd;

            switch (sub)
            {
                case "analyze":
                    {
                        var registry = sp.GetService<SkillRegistryService>();
                        if (registry == null) return "[red]Error:[/] SkillRegistryService not available.";

                        await registry.LoadAsync();
                        var skills = registry.ListSkills();

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("[bold cyan]Skill Registry Diagnostic & Analysis Report[/]");
                        sb.AppendLine(new string('=', 50));
                        sb.AppendLine($"Total Registered Skills: {skills.Count}");
                        if (skills.Any())
                        {
                            int healthy = skills.Count(s => s.Metrics.AverageScore >= 0.8);
                            int needsImprovement = skills.Count(s => s.Metrics.AverageScore < 0.8);
                            double avgScore = skills.Average(s => s.Metrics.AverageScore);

                            sb.AppendLine($"Healthy Skills (Score >= 80%): {healthy}");
                            sb.AppendLine($"Needs Improvement (Score < 80%): {needsImprovement}");
                            sb.AppendLine($"Overall Average Quality Score: {avgScore:P1}");
                        }
                        else
                        {
                            sb.AppendLine("[grey]No skills discovered in the current registry.[/]");
                        }

                        string root = ws ?? Directory.GetCurrentDirectory();
                        var miner = new TrajectoryMiner(root);
                        var patterns = await miner.MineFailurePatternsAsync();
                        var newProposals = await miner.MineAndGenerateProposalsAsync(proposalService);

                        sb.AppendLine();
                        sb.AppendLine("[bold cyan]Trajectory Mining & Failure Analysis[/]");
                        sb.AppendLine(new string('=', 50));
                        sb.AppendLine($"Discovered Failure Patterns: {patterns.Count}");
                        foreach (var pattern in patterns)
                        {
                            sb.AppendLine($"  - {Markup.Escape(pattern)}");
                        }

                        sb.AppendLine();
                        sb.AppendLine($"Auto-generated Skill Proposals: {newProposals.Count}");
                        if (newProposals.Any())
                        {
                            foreach (var prop in newProposals)
                            {
                                sb.AppendLine($"  - [green]{Markup.Escape(prop.Id)}[/]: {Markup.Escape(prop.Title)} (Status: {prop.Status})");
                            }
                        }
                        else
                        {
                            sb.AppendLine("  - [grey]No new proposals generated (either no failures found or all patterns already have proposals).[/]");
                        }

                        return sb.ToString();
                    }

                case "proposals":
                    return await HandleSkillProposals(a, sp);

                case "propose":
                    {
                        if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";
                        if (parts.Length < 3) return "Usage: /skill propose <skillId_or_path> <summary>";

                        string target = parts[1];
                        int targetIdx = a.IndexOf(target) + target.Length;
                        string summary = a.Substring(targetIdx).Trim();
                        if (string.IsNullOrEmpty(summary)) return "Usage: /skill propose <skillId_or_path> <summary>";

                        var proposal = new SkillProposalRecord {
                            Title = summary,
                            Status = SkillProposalStatus.Proposed
                        };

                        if (target.Contains("/") || target.Contains("\\") || target.EndsWith(".md"))
                            proposal.TargetPath = target;
                        else
                            proposal.SkillId = target;

                        try {
                            await proposalService.LoadAsync(ws);
                            proposalService.CreateProposal(ws, proposal);
                            await proposalService.SaveAsync(ws);
                            return $"[green]Proposal '{proposal.Id}' created successfully.[/] Use !skill-proposals to view status.";
                        } catch (Exception ex) {
                            return $"[red]Error creating proposal:[/] {ex.Message}";
                        }
                    }

                case "validate":
                    {
                        if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";
                        if (parts.Length < 2) return "Usage: /skill validate <proposalId>";
                        string proposalId = parts[1];

                        await proposalService.LoadAsync(ws);
                        var proposal = proposalService.GetProposal(proposalId);
                        if (proposal == null) return $"[red]Error:[/] Proposal '{proposalId}' not found.";

                        var validation = proposalService.ValidateProposal(proposal);
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[bold cyan]Proposal Validation Details for {proposalId}:[/]");
                        sb.AppendLine($"  [bold]Title:[/] {proposal.Title}");
                        sb.AppendLine($"  [bold]Validity:[/] {(validation.IsValid ? "[green]VALID[/]" : "[red]INVALID[/]")}");
                        sb.AppendLine($"  [bold]Estimated Pass Rate:[/] {validation.EstimatedPassRate}%");
                        if (validation.Errors.Any())
                        {
                            sb.AppendLine("  [bold red]Errors:[/]");
                            foreach (var err in validation.Errors)
                            {
                                sb.AppendLine($"    - {err}");
                            }
                        }
                        else
                        {
                            sb.AppendLine("  [grey]No errors found.[/]");
                        }
                        return sb.ToString();
                    }

                case "approve":
                    {
                        if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";
                        if (parts.Length < 2) return "Usage: /skill approve <proposalId>";
                        string proposalId = parts[1];

                        try
                        {
                            await proposalService.ApproveProposalAsync(ws, proposalId);
                            return $"[green]Proposal '{proposalId}' has been Approved successfully.[/]";
                        }
                        catch (Exception ex)
                        {
                            return $"[red]Error approving proposal:[/] {ex.Message}";
                        }
                    }

                case "reject":
                    {
                        if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";
                        if (parts.Length < 2) return "Usage: /skill reject <proposalId>";
                        string proposalId = parts[1];

                        try
                        {
                            await proposalService.RejectProposalAsync(ws, proposalId);
                            return $"[green]Proposal '{proposalId}' has been Rejected successfully.[/]";
                        }
                        catch (Exception ex)
                        {
                            return $"[red]Error rejecting proposal:[/] {ex.Message}";
                        }
                    }

                case "apply":
                    {
                        if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";
                        if (parts.Length < 2) return "Usage: /skill apply <proposalId>";
                        string proposalId = parts[1];

                        try
                        {
                            var registry = sp.GetService<SkillRegistryService>();
                            if (registry == null) return "[red]Error:[/] SkillRegistryService not available.";
                            var approvalHandler = sp.GetService<IUserApprovalHandler>() as IRichApprovalHandler;
                            var engine = new SkillApplyEngine(proposalService, registry, approvalHandler);
                            bool success = await engine.ApplyAsync(proposalId, ws);
                            if (success)
                            {
                                return $"[green]Proposal '{proposalId}' has been Applied successfully.[/]";
                            }
                            else
                            {
                                return $"[red]Proposal '{proposalId}' application failed during verification and was reverted.[/]";
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"[red]Error applying proposal:[/] {ex.Message}";
                        }
                    }

                default:
                    return $"[red]Error:[/] Unknown subcommand '{sub}'.";
            }
        }
    }
}
