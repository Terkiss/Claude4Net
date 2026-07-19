using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Commands.Handlers
{
    public static class AgentGoalCommands
    {
        public static Task<string> HandleGoal(string args, IServiceProvider sp)
        {
            string trimmed = args.Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                if (AppState.ActiveGoal == null)
                {
                    AnsiConsole.MarkupLine("[grey]No active goal.[/]");
                    return Task.FromResult("No active goal. Usage: !goal <objective>");
                }

                var g = AppState.ActiveGoal;
                var statusColor = g.Status switch
                {
                    GoalStatus.Active => "cyan",
                    GoalStatus.Completed => "green",
                    GoalStatus.Stopped => "yellow",
                    GoalStatus.Failed => "red",
                    _ => "white"
                };

                var goalTable = new Table().Border(TableBorder.Rounded);
                goalTable.AddColumn("[bold]Property[/]");
                goalTable.AddColumn("[bold]Value[/]");
                goalTable.AddRow("Goal ID", g.Id);
                goalTable.AddRow("Status", $"[{statusColor}]{g.Status}[/]");
                goalTable.AddRow("Objective", Markup.Escape(g.Objective));
                goalTable.AddRow("Turn", $"{g.TurnCount}/{(g.MaxTurns > 0 ? g.MaxTurns.ToString() : "∞")}");
                goalTable.AddRow("No-Progress", $"{g.NoProgressCount}/{g.MaxNoProgressTurns}");
                goalTable.AddRow("Last Tool Calls", g.LastTurnToolCallCount.ToString());
                AnsiConsole.Write(new Panel(goalTable) { Header = new PanelHeader("🎯 Active Goal"), Border = BoxBorder.Rounded });

                return Task.FromResult($"Goal: {g.Status} (Turn {g.TurnCount})");
            }

            if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                if (AppState.ActiveGoal != null)
                {
                    string obj = AppState.ActiveGoal.Objective;
                    GoalDispatcher.Stop();
                    AnsiConsole.MarkupLine($"[bold yellow]Goal stopped:[/] {Markup.Escape(obj)}");
                    return Task.FromResult("Goal cleared. Autonomous loop stopped.");
                }
                return Task.FromResult("No active goal to clear.");
            }

            // !goal <objective> — 새 목표 설정
            int maxTurns = 25;
            string objective = trimmed;

            // --max=N 옵션 파싱
            var maxMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"--max=(\d+)");
            if (maxMatch.Success)
            {
                if (int.TryParse(maxMatch.Groups[1].Value, out int parsed)) maxTurns = parsed;
                objective = trimmed.Replace(maxMatch.Value, "").Trim();
            }

            if (string.IsNullOrWhiteSpace(objective))
            {
                return Task.FromResult("[red]Error:[/] Objective cannot be empty. Usage: !goal <objective>");
            }

            var goal = GoalDispatcher.Activate(objective, maxTurns);
            AnsiConsole.MarkupLine($"[bold cyan]🎯 Goal activated (max {maxTurns} turns):[/]");
            AnsiConsole.MarkupLine($"[italic]{Markup.Escape(objective)}[/]");
            AnsiConsole.MarkupLine("[grey]The agent will autonomously continue until the objective is met, budget is exhausted, or you type !goal clear.[/]");

            return Task.FromResult($"Goal activated: {Markup.Escape(objective)}");
        }

        public static async Task<string> HandleCoordinate(string a, IServiceProvider sp)
        {
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Usage: /coordinate <list|start|status|phase|gate|evidence|approve|reject>";

            string sub = parts[0].ToLowerInvariant();
            var store = CoordinatorStore.Instance;

            switch (sub)
            {
                case "list":
                    var tasks = AppState.GetCoordinatedTasks().ToList();
                    if (!tasks.Any()) return "[grey]No coordinated tasks found.[/]";
                    var table = new System.Text.StringBuilder();
                    table.AppendLine("[bold cyan]Coordinated Tasks:[/]");
                    foreach(var t in tasks)
                    {
                        string scoreColor = t.ReadinessScore > 80 ? "green" : t.ReadinessScore > 40 ? "yellow" : "red";
                        table.AppendLine($"  - [[{t.Id}]] [bold]{t.Title}[/] ({t.CurrentPhase}) [[[{scoreColor}]{t.ReadinessScore:0}%[/]]] - {t.ReviewStatus}");
                    }
                    return table.ToString();

                case "start":
                    {
                        if (parts.Length < 3) return "Usage: /coordinate start <id> <title> [description]";
                        string id = parts[1];
                        string title = parts[2];
                        string desc = title;
                        string? specId = null;

                        int specIndex = Array.FindIndex(parts, p => p.Equals("--spec", StringComparison.OrdinalIgnoreCase));
                        if (specIndex != -1)
                        {
                            if (specIndex + 1 < parts.Length)
                            {
                                specId = parts[specIndex + 1];
                                if (specId.Contains("..") || specId.Contains('/') || specId.Contains('\\') || specId.Contains(':'))
                                {
                                    return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                                }
                                var invalidChars = Path.GetInvalidFileNameChars();
                                if (specId.Any(c => invalidChars.Contains(c)))
                                {
                                    return "[red]Error:[/] Invalid Spec ID. Path traversal or invalid characters detected.";
                                }
                            }
                            else
                            {
                                return "[red]Error:[/] Missing spec ID after --spec option.";
                            }

                            if (specIndex > 3)
                            {
                                desc = string.Join(" ", parts.Skip(3).Take(specIndex - 3));
                            }
                        }
                        else
                        {
                            if (parts.Length > 3)
                            {
                                desc = string.Join(" ", parts.Skip(3));
                            }
                        }

                        SeedSpecRecord? spec = null;
                        if (specId != null)
                        {
                            var specStore = new SeedSpecStore(AppState.CurrentCwd ?? string.Empty);
                            spec = await specStore.LoadAsync(specId);
                            if (spec == null)
                            {
                                return $"[red]Error:[/] Spec '{specId}' not found.";
                            }
                            if (spec.Status != SeedSpecStatus.Locked)
                            {
                                return $"[red]Error: Spec '{specId}' is not in Locked status (Current: {spec.Status}). Only Locked specs can be attached.[/]";
                            }
                        }

                        try {
                            store.CreateTask(id, title, desc);
                            if (spec != null)
                            {
                                store.SyncGatesFromSpec(id, spec);
                            }
                            return $"[green]Task '{title}' started with ID '{id}'. Phase: Planning[/]";
                        } catch (Exception ex) {
                            return $"[red]Error:[/] {ex.Message}";
                        }
                    }

                case "status":
                    if (parts.Length < 2) return $"[red]Error:[/] Usage: /coordinate status <id>";
                    if (!AppState.Tasks.TryGetValue(parts[1], out var st) || st is not CoordinateTask task) return $"[red]Error:[/] Coordinated task '{parts[1]}' not found.";

                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[bold cyan]Task Details: {task.Title} ({task.Id})[/]");
                    sb.AppendLine($"  [bold]Description:[/] {task.Description}");
                    sb.AppendLine($"  [bold]Phase:[/] {task.CurrentPhase}");
                    sb.AppendLine($"  [bold]Review:[/] {task.ReviewStatus}");

                    // 병합 준비도(Readiness) 진행 표시
                    int barWidth = 20;
                    int filled = (int)(task.ReadinessScore / 100 * barWidth);
                    string bar = new string('=', filled) + new string('-', barWidth - filled);
                    string barColor = task.ReadinessScore >= 90 ? "green" : task.ReadinessScore >= 50 ? "yellow" : "blue";
                    sb.AppendLine($"  [bold]Merge Readiness:[/] [{barColor}]{bar}[/] {task.ReadinessScore:0}%");

                    if (task.Blockers.Any())
                    {
                        sb.AppendLine($"  [bold red]Blockers:[/]");
                        foreach (var b in task.Blockers) sb.AppendLine($"    - {b}");
                    }

                    sb.AppendLine($"  [bold]Gates:[/]");
                    if (!task.Gates.Any()) sb.AppendLine("    (No gates defined)");
                    foreach(var g in task.Gates)
                    {
                        string statusIcon = g.IsPassed ? "[green]✔[/]" : "[red]✘[/]";
                        string evidenceInfo = g.Evidences.Any() ? $" ({g.Evidences.Count} Evidence)" : (g.IsEvidenceRequired ? " [red](Evidence Required)[/]" : "");
                        sb.AppendLine($"    - {statusIcon} [bold]{g.Name}[/]: {g.Comments}{evidenceInfo}");
                        if (g.ApprovedBy != null) sb.AppendLine($"      [grey]Approved by: {g.ApprovedBy} at {g.UpdatedAt}[/]");

                        foreach(var ev in g.Evidences)
                        {
                            sb.AppendLine($"      [grey]✔ Evidence: {ev.Summary} (by {ev.Author})[/]");
                        }
                    }

                    sb.AppendLine($"  [bold]History (Last 5):[/]");
                    foreach(var h in task.History.AsEnumerable().Reverse().Take(5)) sb.AppendLine($"    - {h}");

                    return sb.ToString();

                case "phase":
                    if (parts.Length < 3) return "Usage: /coordinate phase <id> <Planning|Execution|Verification|Completed>";
                    if (Enum.TryParse<CoordinatePhase>(parts[2], true, out var newPhase)) {
                        string res = store.TransitionPhase(parts[1], newPhase);
                        return res.StartsWith("Error") ? $"[red]{res}[/]" : $"[green]{res}[/]";
                    }
                    return $"[red]Error:[/] Invalid phase '{parts[2]}'.";

                case "gate":
                    if (parts.Length < 4) return "Usage: /coordinate gate <id> <name> <true|false> [comments]";
                    if (bool.TryParse(parts[3], out bool passed)) {
                        string? gComments = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : null;
                        string user = Environment.UserName;
                        string res = store.UpdateGate(parts[1], parts[2], passed, gComments, user);
                        return res.StartsWith("Error") ? $"[red]{res}[/]" : $"[green]{res}[/]";
                    }
                    return $"[red]Error:[/] Invalid boolean value '{parts[3]}'.";

                case "evidence":
                    if (parts.Length < 4) return "Usage: /coordinate evidence <taskId> <gateName> <summary> [details]";
                    string evTaskId = parts[1];
                    string evGateName = parts[2];
                    string evSummary = parts[3];
                    string? evDetails = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : null;
                    string evAuthor = Environment.UserName;

                    string evRes = store.AddEvidence(evTaskId, evGateName, evAuthor, evSummary, evDetails);
                    return evRes.StartsWith("Error") ? $"[red]{evRes}[/]" : $"[green]{evRes}[/]";

                case "approve":
                    if (parts.Length < 2) return "Usage: /coordinate approve <id> [comments]";
                    string aComments = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Approved via CLI";
                    string aRes = store.SetReview(parts[1], ReviewerDecision.Approved, aComments);
                    return aRes.StartsWith("Error") ? $"[red]{aRes}[/]" : $"[green]{aRes}[/]";

                case "reject":
                    if (parts.Length < 2) return "Usage: /coordinate reject <id> [comments]";
                    string rComments = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Rejected via CLI";
                    string rRes = store.SetReview(parts[1], ReviewerDecision.Rejected, rComments);
                    return rRes.StartsWith("Error") ? $"[red]{rRes}[/]" : $"[yellow]{rRes}[/]";

                default:
                    return $"[red]Unknown subcommand:[/] {sub}";
            }
        }

        public static async Task<string> HandleCheckpoint(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return "[red]Error:[/] Workspace not set.";
            var store = new CheckpointStore(AppState.CurrentCwd, AppState.SessionId);
            var parts = a.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "list";

            if (sub == "list") {
                var cps = await store.ListCheckpointsAsync();
                if (!cps.Any()) return "[grey]No checkpoints found for this session.[/]";
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("ID");
                table.AddColumn("Timestamp");
                table.AddColumn("Reason");
                foreach (var cp in cps) table.AddRow(cp.Id, cp.CreatedAt.ToString("O"), cp.Description ?? "");
                AnsiConsole.Write(table);
                return $"Total {cps.Count} checkpoints.";
            }
            if (sub == "restore" && parts.Length > 1) {
                await store.RestoreCheckpointAsync(parts[1]);
                return "[green]Checkpoint restored.[/]";
            }
            return "Usage: /checkpoint <list | restore <id>>";
        }

        public static async Task<string> HandleHandoff(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return "[red]Error:[/] Workspace not set.";
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "Usage: /handoff <Completed|Blocked|Partial> <summary> [evidenceFiles...]";

            var store = new HandoffStore(AppState.CurrentCwd, AppState.SessionId);
            var record = new SessionHandoffRecord {
                SessionId = AppState.SessionId,
                Status = parts[0],
                Summary = parts[1],
                EvidenceFiles = parts.Skip(2).ToList()
            };
            await store.SaveHandoffAsync(record);
            return "[green]Handoff record saved.[/]";
        }

        public static async Task<string> HandleRoutine(string a, IServiceProvider sp)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd))
                return "[red]Error:[/] Workspace not set. Use /setworkspace <path> first.";

            var store = new RoutineStore(AppState.CurrentCwd);
            var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "[bold cyan]Usage:[/]\n" +
                       "  /routine list\n" +
                       "  /routine show <id>\n" +
                       "  /routine add <id> <name>\n" +
                       "  /routine enable <id>\n" +
                       "  /routine disable <id>\n" +
                       "  /routine delete <id>\n" +
                       "  /routine run <id>";
            }

            string sub = parts[0].ToLowerInvariant();

            bool IsValidRoutineId(string id)
            {
                if (string.IsNullOrWhiteSpace(id)) return false;
                if (id.Contains("..") || id.Contains('/') || id.Contains('\\') || id.Contains(':')) return false;
                var invalidChars = Path.GetInvalidFileNameChars();
                if (id.Any(c => invalidChars.Contains(c))) return false;
                return true;
            }

            switch (sub)
            {
                case "list":
                    {
                        var routines = store.ListRoutines().ToList();
                        if (!routines.Any()) return "[grey]No routines found in workspace.[/]";

                        var table = new Table().Border(TableBorder.Rounded);
                        table.AddColumn("[bold]ID[/]");
                        table.AddColumn("[bold]Name[/]");
                        table.AddColumn("[bold]Status[/]");
                        table.AddColumn("[bold]Trigger[/]");
                        table.AddColumn("[bold]Actions[/]");
                        table.AddColumn("[bold]Permission Mode[/]");
                        table.AddColumn("[bold]Last Run[/]");

                        foreach (var r in routines.OrderBy(x => x.Id))
                        {
                            string statusStr = r.IsEnabled ? "[green]Enabled[/]" : "[red]Disabled[/]";
                            string lastRunStr = r.LastRun.HasValue ? r.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Never";
                            table.AddRow(
                                Markup.Escape(r.Id),
                                Markup.Escape(r.Name),
                                statusStr,
                                Markup.Escape(r.Trigger?.Kind.ToString() ?? "Manual"),
                                r.Actions?.Count.ToString() ?? "0",
                                Markup.Escape(r.PermissionMode.ToString()),
                                lastRunStr
                            );
                        }

                        AnsiConsole.Write(table);
                        return $"Total {routines.Count} routines listed.";
                    }

                case "show":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /routine show <id>";
                        }
                        string id = parts[1];
                        if (!IsValidRoutineId(id))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }
                        var routine = await store.LoadAsync(id);
                        if (routine == null)
                        {
                            return $"[red]Error:[/] Routine '{id}' not found.";
                        }

                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[bold cyan]Routine:[/] {Markup.Escape(routine.Name)} ([bold]{Markup.Escape(routine.Id)}[/])");
                        sb.AppendLine($"[bold]Status:[/] {(routine.IsEnabled ? "[green]Enabled[/]" : "[red]Disabled[/]")}");
                        sb.AppendLine($"[bold]Trigger:[/] {routine.Trigger?.Kind} {Markup.Escape(routine.Trigger?.Expression ?? "")}");
                        sb.AppendLine($"[bold]Permission Mode:[/] {routine.PermissionMode}");
                        sb.AppendLine($"[bold]Workspace Dir:[/] {Markup.Escape(routine.WorkspaceDir ?? "(None)")}");
                        sb.AppendLine($"[bold]Last Run:[/] {(routine.LastRun.HasValue ? routine.LastRun.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}");
                        sb.AppendLine();
                        sb.AppendLine("[bold yellow]Actions:[/]");
                        if (routine.Actions == null || routine.Actions.Count == 0)
                        {
                            sb.AppendLine("  (No actions defined)");
                        }
                        else
                        {
                            for (int i = 0; i < routine.Actions.Count; i++)
                            {
                                var action = routine.Actions[i];
                                sb.AppendLine($"  {i + 1}. [{action.Kind}] {Markup.Escape(action.Payload)}");
                            }
                        }
                        return sb.ToString();
                    }

                case "add":
                    {
                        if (parts.Length < 3)
                        {
                            return "[red]Error:[/] Usage: /routine add <id> <name>";
                        }
                        string addId = parts[1];
                        if (!IsValidRoutineId(addId))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }

                        var existing = await store.LoadAsync(addId);
                        if (existing != null)
                        {
                            return $"[red]Error:[/] Routine with ID '{addId}' already exists.";
                        }

                        string name = string.Join(" ", parts.Skip(2)).Trim('\"');

                        var newRoutine = new RoutineDefinition
                        {
                            Id = addId,
                            Name = name,
                            IsEnabled = false,
                            Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Manual },
                            Actions = new List<RoutineAction>(),
                            PermissionMode = PermissionMode.Prompt,
                            WorkspaceDir = AppState.CurrentCwd,
                            CreatedAt = DateTimeOffset.UtcNow,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };

                        await store.SaveAsync(newRoutine);
                        return $"[green]Routine '{name}' created successfully with ID '{addId}'.[/]";
                    }

                case "enable":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /routine enable <id>";
                        }
                        string id = parts[1];
                        if (!IsValidRoutineId(id))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }
                        var routine = await store.LoadAsync(id);
                        if (routine == null)
                        {
                            return $"[red]Error:[/] Routine '{id}' not found.";
                        }

                        routine.IsEnabled = true;
                        await store.SaveAsync(routine);
                        return $"[green]Routine '{id}' has been enabled.[/]";
                    }

                case "disable":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /routine disable <id>";
                        }
                        string id = parts[1];
                        if (!IsValidRoutineId(id))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }
                        var routine = await store.LoadAsync(id);
                        if (routine == null)
                        {
                            return $"[red]Error:[/] Routine '{id}' not found.";
                        }

                        routine.IsEnabled = false;
                        await store.SaveAsync(routine);
                        return $"[green]Routine '{id}' has been disabled.[/]";
                    }

                case "delete":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /routine delete <id>";
                        }
                        string id = parts[1];
                        if (!IsValidRoutineId(id))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }
                        var routine = await store.LoadAsync(id);
                        if (routine == null)
                        {
                            return $"[red]Error:[/] Routine '{id}' not found.";
                        }

                        await store.DeleteAsync(id);
                        return $"[green]Routine '{id}' has been deleted.[/]";
                    }

                case "run":
                    {
                        if (parts.Length < 2)
                        {
                            return "[red]Error:[/] Usage: /routine run <id>";
                        }
                        string id = parts[1];
                        if (!IsValidRoutineId(id))
                        {
                            return "[red]Error:[/] Invalid Routine ID. Path traversal or illegal characters detected.";
                        }
                        var routine = await store.LoadAsync(id);
                        if (routine == null)
                        {
                            return $"[red]Error:[/] Routine '{id}' not found.";
                        }

                        if (!routine.IsEnabled)
                        {
                            return $"[red]Error:[/] Routine '{id}' is disabled.";
                        }

                        var runner = new RoutineRunner(store, new PermissionEnforcer(), new PathSafetyEvaluator(), sp);
                        var result = await runner.RunAsync(id, AppState.CurrentCwd, AppState.CurrentPermissionMode);
                        if (result.Success)
                        {
                            return $"[green]Routine '{id}' executed successfully.[/] Run ID: {result.RunId}";
                        }
                        else
                        {
                            return $"[red]Routine '{id}' execution failed: {result.Error}[/]";
                        }
                    }

                default:
                    return $"[red]Error:[/] Unknown subcommand '{sub}'.";
            }
        }
    }
}
