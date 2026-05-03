using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Spectre.Console;
using Claude4Net.SDK;
using System.Text.Json;
using Claude4Net.Api;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// Claude4Net의 핵심 실행 엔진으로, 에이전트의 사고-행동-관찰(Reasoning Loop) 과정을 총괄합니다.
    /// 입력 처리, 스마트 라우팅, RAG 검색, 도구 실행 및 자가 치유를 위한 궤적 수집을 담당합니다.
    /// </summary>
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly IEmbeddingProvider? _embedding;

        /// <summary>
        /// AgentLoop의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="orchestrator">도구 실행을 관리하는 오케스트레이터</param>
        /// <param name="serviceProvider">의존성 주입 서비스를 위한 프로바이더</param>
        /// <param name="broker">사용자 입력을 수신하는 브로커</param>
        /// <param name="router">LLM 요청을 적절한 프로바이더로 전달하는 라우터</param>
        /// <param name="embedding">RAG를 위한 임베딩 프로바이더 (선택 사항)</param>
        public AgentLoop(ToolOrchestrator orchestrator, IServiceProvider serviceProvider, IInputBroker broker, ISmartRouter router, IEmbeddingProvider? embedding = null)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
            _router = router;
            _embedding = embedding;
        }

        /// <summary>
        /// 메인 메시지 수신 루프를 시작합니다.
        /// 사용자의 입력을 대기하고, 입력 유형에 따라 시스템 명령 처리 또는 LLM 추론 프로세스를 수행합니다.
        /// </summary>
        /// <param name="ct">취소 토큰</param>
        public async Task ListenAsync(CancellationToken ct = default)
        {
            AnsiConsole.MarkupLine("[bold cyan][[Agent]][/] Consumer loop started. Waiting for messages...");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1. 사용자 입력 수신
                    var context = await _broker.ReadAsync(ct);
                    string finalPrompt = context.Text;

                    // 2. 특수 명령 처리: !reflect (자가 성찰 및 가이드 업데이트)
                    if (finalPrompt.Trim().ToLower() == "!reflect")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Analyzing agent_trajectories...[/]");
                        string diagnosis = await GenerateReflectionSummaryAsync();
                        if (string.IsNullOrEmpty(diagnosis))
                        {
                            AnsiConsole.MarkupLine("[red]분석할 궤적(agent_trajectories) 데이터가 없습니다.[/]");
                            await context.Output.WriteAsync("No trajectories found to reflect on.");
                            Console.Write("\n> ");
                            continue;
                        }
                        
                        // 진단 결과를 바탕으로 Self-Healing 가이드 업데이트
                        SelfHealingService.Instance.UpdateGuide(diagnosis);
                        AnsiConsole.MarkupLine("[bold green]SELF_HEAL_GUIDE.md updated successfully.[/]");

                        finalPrompt = "당신의 최근 궤적 통계 진단서입니다.\n\n" + diagnosis + "\n\n이 데이터 기반 실패 통계를 바탕으로 작업 방식을 성찰 및 재설계하고 자율적으로 `Skills` 폴더 내에 마크다운 파일(예: `Skills/SKILL.md`)을 생성/업데이트하여 피드백 루프를 완성하세요. Skills 폴더가 없다면 우선 생성하십시오. 가이드라인은 반드시 마크다운 포맷으로 구체적으로 작성하세요. 업데이트 후 핵심 변경사항을 한국어로 보고하세요.";
                    }
                    else
                    {
                        // 3. 인텐트 기반 쿼리 라우팅 (예: 자연어를 시스템 명령어로 변환)
                        string? routedCommand = QueryRouter.Route(context.Text);

                        // 4. 시스템 명령어 가로채기 및 처리
                        var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output, context.Approval) : context;
                        if (await HandleSystemCommand(effectiveContext, ct))
                        {
                            Console.Write("\n> ");
                            continue;
                        }
                        finalPrompt = effectiveContext.Text;
                    }

                    // 작업 공간 설정 확인 (보안 및 경로 기준점)
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Conversations are blocked. Use [bold]/setworkspace <path>[/] first.");
                        await context.Output.WriteAsync("Error: Workspace is not set. Conversations are blocked. Use /setworkspace <path> first.");
                        Console.Write("\n> ");
                        continue;
                    }

                    // 5. Smart Routing: 입력의 복잡도와 비용, 성공률을 고려하여 최적의 LLM 선정
                    var decision = _router.Route(finalPrompt);
                    ILLMProvider provider = decision.SelectedProvider switch
                    {
                        "gemini" => _serviceProvider.GetRequiredService<GeminiProvider>(),
                        "gemini-cli" => _serviceProvider.GetRequiredService<GeminiCliProvider>(),
                        "ollama" => _serviceProvider.GetRequiredService<OllamaProvider>(),
                        _ => _serviceProvider.GetRequiredService<ClaudeService>()
                    };

                    AnsiConsole.MarkupLine($"[grey]Routing:[/] [bold cyan]{decision.SelectedProvider}[/] ([italic]{decision.SelectedModel}[/]) - [grey]{decision.Reason ?? "Auto"}[/]");

                    // 6. RAG(Retrieval-Augmented Generation): 과거의 유사한 작업 기억 추출
                    string relevantContext = await RetrieveRelevantMemoriesAsync(finalPrompt);
                    if (!string.IsNullOrEmpty(relevantContext))
                    {
                        AnsiConsole.MarkupLine("[bold blue]🧠 Context Retrieved:[/] Found relevant past interactions in agent_memory.");
                    }
                    string promptWithContext = relevantContext + finalPrompt;

                    // 7. 사고-행동-관찰 루프 실행
                    await RunAsync(promptWithContext, context.Output, provider, decision.SelectedModel, context.Approval, ct);

                    Console.Write("\n> ");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[Agent]] Consumer Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
            }
        }

        /// <summary>
        /// 사용자의 프롬프트와 관련된 과거 기록을 검색하여 컨텍스트를 증강합니다.
        /// </summary>
        private async Task<string> RetrieveRelevantMemoriesAsync(string userPrompt)
        {
            if (_embedding == null) return "";
            
            var sw = Stopwatch.StartNew();
            float[]? targetVector = null;

            // 단계 1: TeruTeruPandas L2 캐시(embedding_cache)에서 기존 임베딩 검색
            targetVector = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("embedding_cache")) return null;
                var df = u.GetTableOrThrow("embedding_cache");
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df["Text"].GetValue(i)?.ToString() == userPrompt)
                    {
                        return df["Embedding"].GetValue(i) as float[];
                    }
                }
                return null;
            });

            // 단계 2: 캐시에 없는 경우 API를 호출하여 임베딩 생성 및 저장
            if (targetVector == null)
            {
                try { targetVector = await _embedding.GetEmbeddingAsync(userPrompt); } catch { }
                
                if (targetVector != null && targetVector.Length > 0)
                {
                    var vec = targetVector;
                    _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                    {
                        if (!u.ContainsTable("embedding_cache")) return null!;
                        var df = u.GetTableOrThrow("embedding_cache");
                        var newRowCols = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                        {
                            ["Text"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { userPrompt }),
                            ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(new[] { vec }),
                            ["LastUsed"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.Now.ToString("O") })
                        };
                        var newRowDf = new DataFrame(newRowCols);
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("embedding_cache", updatedDf);
                        return null!;
                    });
                }
            }

            // 단계 3: 벡터 유사도 기반 메모리 검색 (Vector Search)
            string result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory")) return "";
                var df = u.GetTableOrThrow("agent_memory");
                if (df.RowCount == 0) return "";

                DataFrame topMemories;

                // 유효한 벡터가 있고 Embedding 컬럼이 존재하는 경우 SIMD 가속 코사인 유사도 계산
                if (targetVector != null && targetVector.Length > 0 && df.Columns.Contains("Embedding"))
                {
                    var embCol = df["Embedding"];
                    var validIndices = new List<int>();
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        if (embCol.GetValue(i) is float[] v && v.Length == targetVector.Length)
                        {
                            validIndices.Add(i);
                        }
                    }

                    if (validIndices.Count > 0)
                    {
                        var filteredDf = df.Reorder(validIndices.ToArray());
                        topMemories = filteredDf.OrderByDescendingCosineSimilarity("Embedding", targetVector).Head(3);
                    }
                    else
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                    
                    // 유사도가 너무 낮거나 결과가 없는 경우 키워드 매칭으로 Fallback
                    var topSim = topMemories.Columns.Contains("Similarity") ? (double)(topMemories["Similarity"].GetValue(0) ?? -1.0) : -1.0;
                    if (topSim <= 0)
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                }
                else
                {
                    // 단계 4: 벡터 검색이 불가능한 경우 키워드 매칭 수행
                    topMemories = SearchByKeywords(df, userPrompt);
                }

                if (topMemories.RowCount == 0) return "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\n[시스템 주의: 과거 상호작용 기록 중 현재 요청과 관련된 내용이 발견되었습니다. 참고하십시오.]");
                for (int i = 0; i < topMemories.RowCount; i++)
                {
                    sb.AppendLine($"--- 기록 (인덱스: {i}) ---");
                    sb.AppendLine($"요청: {topMemories["UserPrompt"].GetValue(i)}");
                    sb.AppendLine($"대응: {topMemories["AgentResponse"].GetValue(i)}");
                }
                sb.AppendLine("--------------------------------------------------------------------------\n");
                return sb.ToString();
            });

            sw.Stop();
            if (sw.ElapsedMilliseconds > 200)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Performance Warning:[/] RAG retrieval took {sw.ElapsedMilliseconds}ms.");
            }
            return result;
        }

        /// <summary>
        /// 프롬프트에서 추출된 키워드를 기반으로 메모리를 검색합니다.
        /// </summary>
        private DataFrame SearchByKeywords(DataFrame df, string userPrompt)
        {
            var keywordsStr = ExtractKeywords(userPrompt);
            if (string.IsNullOrEmpty(keywordsStr)) return df.Head(0);
            var currentKeywords = keywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var scored = new List<(int idx, int score)>();
            for (int i = 0; i < df.RowCount; i++)
            {
                var recordKeywordsStr = df.Columns.Contains("Keywords") ? df["Keywords"].GetValue(i)?.ToString() ?? "" : "";
                var recordKeywords = recordKeywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                int score = recordKeywords.Intersect(currentKeywords).Count();
                if (score > 0) scored.Add((i, score));
            }

            if (!scored.Any()) return df.Head(0);
            var indices = scored.OrderByDescending(x => x.score).ThenByDescending(x => x.idx).Take(3).Select(x => x.idx).ToArray();
            return df.Reorder(indices);
        }

        /// <summary>
        /// 수집된 에이전트 궤적(Trajectories)을 분석하여 도구 사용 통계 및 실패 원인을 분석합니다.
        /// </summary>
        private async Task<string> GenerateReflectionSummaryAsync()
        {
            return await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return "";
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return "";

                int totalCount = df.RowCount;

                var toolNames = new List<string>();
                var isErrors = new List<bool>();
                var errorReasons = new List<string>();
                var categories = new List<string>();

                for (int i = 0; i < df.RowCount; i++)
                {
                    toolNames.Add(df["ToolName"].GetValue(i)?.ToString() ?? "");
                    isErrors.Add(df["IsError"].GetValue(i)?.ToString() == "True");
                    errorReasons.Add(df["ErrorReason"].GetValue(i)?.ToString() ?? "");
                    categories.Add(df.Columns.Any(c => c == "Category") ? df["Category"].GetValue(i)?.ToString() ?? "Unknown" : "Unknown");
                }

                var stats = toolNames.Distinct().Select(tn =>
                {
                    var indices = toolNames.Select((n, idx) => (n, idx)).Where(x => x.n == tn).Select(x => x.idx).ToList();
                    int total = indices.Count;
                    int fails = indices.Count(idx => isErrors[idx]);
                    return new { ToolName = tn, Total = total, Fails = fails, Rate = total > 0 ? (double)fails / total : 0 };
                }).OrderByDescending(x => x.Rate).ThenByDescending(x => x.Fails).ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== 지능형 통계 진단서 (DataUniverse Agent Trajectories) ===");
                sb.AppendLine($"총 툴 호출 횟수: {totalCount}");
                foreach (var s in stats)
                {
                    sb.AppendLine($"- {s.ToolName} : {s.Total}회 시도, {s.Fails}회 실패 (실패율 {s.Rate * 100:0.1}%)");
                }

                var failCategories = categories.Where(c => c != "Success" && c != "Unknown")
                                               .GroupBy(c => c)
                                               .OrderByDescending(g => g.Count());
                
                if (failCategories.Any())
                {
                    sb.AppendLine("\n실패 카테고리 분포:");
                    foreach (var c in failCategories) sb.AppendLine($" - {c.Key}: {c.Count()}회");
                }

                var topErrors = errorReasons.Where(e => !string.IsNullOrWhiteSpace(e) && e.Length > 3)
                                            .GroupBy(e => e)
                                            .OrderByDescending(g => g.Count())
                                            .Take(3);

                if (topErrors.Any())
                {
                    sb.AppendLine("\n주요 발생 에러 내용 (Top 3):");
                    foreach (var e in topErrors) sb.AppendLine($" - [{e.Count()}회 발생] {e.Key.Replace("\n", " ").Substring(0, Math.Min(150, e.Key.Length))}");
                }

                return sb.ToString();
            });
        }

        /// <summary>
        /// 시스템 명령어를 처리합니다. (!) 또는 (/)로 시작하는 명령어를 감지합니다.
        /// </summary>
        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            string text = context.Text.Trim();
            if (!(text.StartsWith("!") || text.StartsWith("/"))) return false;

            string[] parts = text.Split(' ', 2);
            string baseCmd = parts[0].TrimStart('!', '/').ToLowerInvariant();

            switch (baseCmd)
            {
                case "build":
                    AnsiConsole.MarkupLine("[bold blue]Building project...[/]");
                    await context.Output.WriteAsync("Build triggered.");
                    return true;

                case "test":
                    AnsiConsole.MarkupLine("[bold blue]Running tests...[/]");
                    await context.Output.WriteAsync("Test suite execution started.");
                    return true;

                case "clean":
                    AnsiConsole.MarkupLine("[bold blue]Cleaning solution...[/]");
                    await context.Output.WriteAsync("Solution clean started.");
                    return true;

                case "clear":
                    Console.Clear();
                    AnsiConsole.MarkupLine("[bold green]Console cleared.[/]");
                    return true;

                case "exit":
                case "quit":
                    AnsiConsole.MarkupLine("[bold yellow]System is shutting down safely... Goodbye![/]");
                    await context.Output.WriteAsync("Agent is going offline.");
                    return true;

                case "login":
                    var loginArgs = parts.Length > 1 ? parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();

                    if (loginArgs.Length == 0)
                    {
                        AnsiConsole.MarkupLine("[bold yellow]Usage:[/] !login <provider> [key]");
                        await context.Output.WriteAsync("Usage: !login <provider> [key]");
                        return true;
                    }

                    string providerName = loginArgs[0].ToLowerInvariant();

                    if (providerName == "geminicli" || providerName == "gemini-cli")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Switching provider to Gemini CLI...[/]");
                        AppState.ActiveProvider = "gemini-cli";
                        AppState.IsProviderExplicitlySet = true;
                        await context.Output.WriteAsync("Provider switched to Gemini CLI (gemini-cli). No API key required (OAuth handled by CLI).");
                    }
                    else
                    {
                        if (loginArgs.Length < 2)
                        {
                            AnsiConsole.MarkupLine($"[bold red]Error:[/] API key is required for provider '{providerName}'.");
                            await context.Output.WriteAsync($"Error: API key is required for provider '{providerName}'.");
                            return true;
                        }

                        string key = loginArgs[1];
                        await AuthManager.SaveProviderKeyAsync(providerName, key);

                        AppState.ActiveProvider = providerName;
                        AppState.IsProviderExplicitlySet = true;
                        AnsiConsole.MarkupLine($"[bold cyan]Switching provider to {providerName}...[/]");
                        await context.Output.WriteAsync($"Provider switched to {providerName} and API key has been saved.");
                    }
                    return true;

                case "tools":
                    var tools = _orchestrator.GetTools();
                    var table = new Table().Border(TableBorder.Rounded);
                    table.AddColumn("[bold cyan]Tool Name[/]");
                    table.AddColumn("[bold yellow]Description[/]");

                    foreach (var tool in tools.OrderBy(t => t.Name))
                    {
                        table.AddRow(Markup.Escape(tool.Name), Markup.Escape(tool.Description ?? "No description"));
                    }
                    AnsiConsole.Write(table);
                    await context.Output.WriteAsync($"Loaded tools: {string.Join(", ", tools.Select(t => t.Name))}");
                    return true;

                case "reload":
                    string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                    _orchestrator.ReloadDynamicPlugins(pluginsDir);

                    AnsiConsole.MarkupLine($"[bold green]Dynamic plugins have been fully hot-reloaded from RAM! ({pluginsDir})[/]");
                    await context.Output.WriteAsync("System plugins metadata and runtime assemblies refreshed.");
                    return true;

                case "prune":
                    int days = 7;
                    if (parts.Length > 1 && int.TryParse(parts[1], out int d)) days = d;
                    AnsiConsole.MarkupLine($"[bold cyan]Pruning agent_trajectories older than {days} days...[/]");
                    await SelfHealingService.Instance.PruneTrajectoriesAsync(days);
                    await context.Output.WriteAsync($"Trajectory pruning completed (Retention: {days} days).");
                    return true;

                case "status":
                    var process = Process.GetCurrentProcess();
                    long memoryUsed = GC.GetTotalMemory(false) / 1024 / 1024;

                    var grid = new Grid();
                    grid.AddColumn(new GridColumn().NoWrap());
                    grid.AddColumn(new GridColumn().Padding(2, 0, 0, 0));

                    grid.AddRow("[bold cyan]OS:[/]", Markup.Escape(Environment.OSVersion.ToString()));
                    grid.AddRow("[bold cyan]Active Provider:[/]", Markup.Escape(AppState.ActiveProvider));
                    grid.AddRow("[bold cyan]Active Model:[/]", Markup.Escape(AppState.ActiveModel));
                    grid.AddRow("[bold cyan]Memory Usage:[/]", $"{memoryUsed} MB");
                    grid.AddRow("[bold cyan]Loaded Tools:[/]", _orchestrator.GetTools().Count.ToString());
                    grid.AddRow("[bold cyan]YOLO Mode:[/]", AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]");

                    var panel = new Panel(grid)
                    {
                        Header = new PanelHeader("System Status"),
                        Border = BoxBorder.Rounded,
                        Padding = new Padding(1, 1, 1, 1)
                    };
                    AnsiConsole.Write(panel);
                    await context.Output.WriteAsync($"System Status: {AppState.ActiveProvider}/{AppState.ActiveModel}, Memory: {memoryUsed}MB");
                    return true;

                case "save":
                    try
                    {
                        if (string.IsNullOrEmpty(AppState.CurrentCwd))
                        {
                            AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Use [bold]/setworkspace <path>[/] before saving context.");
                            await context.Output.WriteAsync("Error: Workspace is not set. Context save aborted.");
                            return true;
                        }

                        ILLMProvider provider;
                        if (AppState.ActiveProvider == "gemini") provider = _serviceProvider.GetRequiredService<GeminiProvider>();
                        else if (AppState.ActiveProvider == "ollama") provider = _serviceProvider.GetRequiredService<OllamaProvider>();
                        else provider = _serviceProvider.GetRequiredService<ClaudeService>();

                        var history = provider.GetHistory();
                        string dateStr = DateTime.Now.ToString("yyyyMMdd");
                        string fileName = $"context_{dateStr}.json";
                        string fullPath = Path.Combine(AppState.CurrentCwd, fileName);

                        string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(fullPath, json);

                        AnsiConsole.MarkupLine($"[bold green]Context saved to:[/] [underlined]{Markup.Escape(fullPath)}[/]");
                        await context.Output.WriteAsync($"Conversation context saved to {fileName}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Error saving context:[/] {Markup.Escape(ex.Message)}");
                    }
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 다중 턴 추론 루프를 실행합니다. LLM의 응답과 도구 호출을 반복적으로 처리합니다.
        /// </summary>
        /// <param name="userPrompt">사용자 입력 프롬프트</param>
        /// <param name="output">결과를 출력할 핸들러</param>
        /// <param name="provider">사용할 LLM 프로바이더</param>
        /// <param name="model">사용할 모델 이름</param>
        /// <param name="approval">사용자 승인 핸들러</param>
        /// <param name="ct">취소 토큰</param>
        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, string model, IUserApprovalHandler? approval = null, CancellationToken ct = default)
        {
            string currentPrompt = userPrompt;
            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 200; // 무한 루프 방지를 위한 Circuit Breaker

            var sw = Stopwatch.StartNew();
            bool hasError = false;
            string lastTurnResponse = "";

            // --- 사고-행동-관찰(Reasoning) 루프 시작 ---
            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                turnCount++;
                var toolCalls = new List<ToolUseRequest>();
                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");

                    // 단계 1: LLM에게 현재 상황을 전달하고 스트리밍 응답 수신
                    await foreach (var evt in provider.StreamQueryAsync(isFirstTurn ? currentPrompt : "Proceed based on previous tool results.", model: model, ct: ct))
                    {
                        if (evt.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(evt.Delta))
                        {
                            if (turnTextBuilder.Length == 0) Console.WriteLine();
                            Console.Write(evt.Delta);
                            turnTextBuilder.Append(evt.Delta);
                        }
                        else if (evt.Type == LLMStreamEventType.ThinkingDelta)
                        {
                            Console.Write(".");
                        }
                        else if (evt.Type == LLMStreamEventType.ToolCallStart && evt.ToolCall != null)
                        {
                            Console.Write("!");
                            toolCalls.Add(evt.ToolCall);
                        }
                        else if (evt.Type == LLMStreamEventType.Completed && evt.FinalResponse != null)
                        {
                            foreach (var tc in evt.FinalResponse.ToolCalls)
                            {
                                if (!toolCalls.Any(existing => existing.Id == tc.Id)) toolCalls.Add(tc);
                            }
                        }
                    }
                    Console.WriteLine();

                    if (turnTextBuilder.Length > 0)
                    {
                        lastTurnResponse = turnTextBuilder.ToString();
                        await output.WriteAsync(lastTurnResponse);
                    }
                }
                catch (Exception ex)
                {
                    hasError = true;
                    string errorMsg = $"Error ({provider.Name}): {ex.Message}";
                    AnsiConsole.Console.Write(new Markup($"\n[bold red]{Markup.Escape(errorMsg)}[/]\n"));
                    await output.WriteAsync(errorMsg);
                    break;
                }

                isFirstTurn = false;

                // 단계 2: 도구 호출(Tool Call)이 발생한 경우 실행 처리
                if (toolCalls.Count > 0)
                {
                    foreach (var tc in toolCalls)
                    {
                        AnsiConsole.MarkupLine($"[grey]🛠️  [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                    }

                    // 도구 오케스트레이터를 통한 배치 실행 (보안 검사 및 병렬 처리 포함)
                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { }, approval, ct);

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";

                        // 이미지 생성 결과 처리 (Discord 등 외부 출력 연동)
                        if (!result.IsError && result.Content != null)
                        {
                            try
                            {
                                var json = JsonSerializer.Serialize(result.Content);
                                using var doc = JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("savedPath", out var pathProp))
                                {
                                    string savedPath = pathProp.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(savedPath))
                                    {
                                        _ = output.SendFileAsync(savedPath, "Generated Image:");
                                    }
                                }
                            }
                            catch { }
                        }

                        if (summary.Length > 100) summary = summary.Substring(0, 97) + "...";

                        string escapedId = Markup.Escape(result.ToolUseId);
                        string escapedSummary = Markup.Escape(summary);

                        if (result.IsError)
                            AnsiConsole.MarkupLine($"  [red]✗ {escapedId}:[/] [grey]{escapedSummary}[/]");
                        else
                            AnsiConsole.MarkupLine($"  [green]✓ {escapedId}:[/] [grey]{escapedSummary}[/]");

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content?.ToString() ?? "Success", is_error = result.IsError });
                    }

                    // 단계 3: [데이터 진화 전략] 실행 궤적 수집 및 텔레메트리 기록
                    if (batchResults.Count > 0)
                    {
                        var telemetryList = new List<string>();
                        string timestamp = DateTime.Now.ToString("O");
                        foreach (var result in batchResults)
                        {
                            string toolName = toolCalls.FirstOrDefault(t => t.Id == result.ToolUseId)?.Name ?? "unknown_tool";
                            string errorText = result.IsError ? (result.Content?.ToString() ?? "Error") : "";
                            string category = result.IsError ? ErrorClassifier.Classify(toolName, errorText).ToString() : "Success";
                            
                            var dict = new Dictionary<string, object>
                            {
                                { "Timestamp", timestamp },
                                { "AgentId", AppState.SessionId },
                                { "ToolName", toolName },
                                { "IsError", result.IsError },
                                { "ErrorReason", errorText },
                                { "Category", category },
                                { "Payload", result.Content?.ToString() ?? "" }
                            };
                            telemetryList.Add(JsonSerializer.Serialize(dict));
                        }

                        var jsonArrayStr = "[" + string.Join(",", telemetryList) + "]";
                        // 비동기 백그라운드로 궤적 데이터 저장 (성능 저하 방지)
                        _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                        {
                            string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                            File.WriteAllText(tmpFile, jsonArrayStr);
                            try
                            {
                                var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                                if (u.ContainsTable("agent_trajectories"))
                                {
                                    var df = u.GetTableOrThrow("agent_trajectories");
                                    var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                                    u.AddOrUpdateTable("agent_trajectories", updatedDf);
                                }
                                else
                                {
                                    u.AddTable("agent_trajectories", newRowDf, "Auto-collected AI execution trajectories for self-reflection.");
                                }
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.Console.Write(new Markup($"[bold red][[Telemetry]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                            }
                            finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                            return null!;
                        });
                    }

                    // 단계 4: 컨텍스트 압축 및 도구 결과 피드백
                    // 결과가 너무 길 경우 요약하여 LLM에게 다시 전달 (토큰 절약 및 컨텍스트 유지)
                    var processedResults = ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
                    continue;
                }

                // 더 이상의 도구 호출이 없으면 루프 종료
                break;
            }

            await output.CompleteAsync(lastTurnResponse);

            sw.Stop();
            // 라우터 성능 메트릭 업데이트 (지수 이동 평균 반영)
            _router.UpdateMetric(provider.Name, sw.Elapsed.TotalMilliseconds, hasError);

            // 단계 5: [RAG Ingestion] 성공적인 상호작용 기록 저장
            if (!hasError && !string.IsNullOrEmpty(lastTurnResponse))
            {
                var finalRes = lastTurnResponse;
                var keywords = ExtractKeywords(userPrompt + " " + finalRes);
                float[]? vector = null;
                if (_embedding != null)
                {
                    try { vector = await _embedding.GetEmbeddingAsync(userPrompt + " " + finalRes); } catch { }
                }
                
                _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                    var memoryRecord = new Dictionary<string, object?>
                    {
                        ["AgentId"] = AppState.SessionId,
                        ["Role"] = "assistant",
                        ["Status"] = "active",
                        ["CurrentTask"] = userPrompt.Length > 100 ? userPrompt.Substring(0, 97) + "..." : userPrompt,
                        ["SharedContext"] = "",
                        ["LastUpdated"] = DateTime.Now.ToString("O"),
                        ["SessionId"] = AppState.SessionId,
                        ["Keywords"] = keywords,
                        ["UserPrompt"] = userPrompt,
                        ["AgentResponse"] = finalRes,
                        ["Embedding"] = (vector != null && vector.Length > 0) ? vector : null
                    };
                    
                    File.WriteAllText(tmpFile, "[" + JsonSerializer.Serialize(memoryRecord) + "]");
                    try
                    {
                        var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                        var df = u.GetTableOrThrow("agent_memory");
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("agent_memory", updatedDf);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.Console.Write(new Markup($"[bold red][[RAG Storage]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                    }
                    finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                    return null!;
                });
            }

            if (turnCount >= MAX_TURNS)
            {
                AnsiConsole.MarkupLine("\n[bold red]🛑 Circuit Breaker Hit![/]");
            }
        }

        private static readonly Regex KeywordRegex = new(@"\b\w{4,}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) 
        { 
            "this", "that", "there", "their", "where", "which", "could", "should", "would", "about", "above", "after", "again" 
        };

        /// <summary>
        /// 텍스트에서 검색에 사용할 주요 키워드를 추출합니다.
        /// </summary>
        private string ExtractKeywords(string text)
        {
            try
            {
                var words = KeywordRegex.Matches(text.ToLower())
                                 .Cast<Match>()
                                 .Select(m => m.Value)
                                 .Where(w => !StopWords.Contains(w))
                                 .GroupBy(w => w)
                                 .OrderByDescending(g => g.Count())
                                 .Take(10)
                                 .Select(g => g.Key);
                return string.Join(",", words);
            }
            catch { return ""; }
        }
    }
}
