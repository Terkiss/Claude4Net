using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Claude4Net.SDK.Terukirdo;
using Claude4Net.Runtime.Terukirdo;

namespace Claude4Net.Commands.Handlers
{
    /// <summary>
    /// 1급 메이드 오케스트레이터 테르키르도 전용 CLI 슬래시 명령어 핸들러
    /// </summary>
    public static class TerukirdoCommands
    {
        public static async Task<string> HandleMaid(string args, IServiceProvider sp)
        {
            var orchestrator = sp.GetService<ITerukirdoOrchestrator>() ?? new TerukirdoOrchestrator();
            string[] parts = (args ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";

            switch (sub)
            {
                case "mode":
                    if (parts.Length < 2)
                    {
                        AnsiConsole.MarkupLine($"[yellow]현재 모드:[/] [bold cyan]{orchestrator.CurrentMode}[/]");
                        AnsiConsole.MarkupLine("[grey]사용법: /maid mode <companion | secretary | orchestrator | controller>[/]");
                        return string.Empty;
                    }
                    string modeStr = parts[1].ToLowerInvariant();
                    TerukirdoMode newMode = modeStr switch
                    {
                        "companion" => TerukirdoMode.Companion,
                        "secretary" or "maidsecretary" => TerukirdoMode.MaidSecretary,
                        "orchestrator" or "orch" => TerukirdoMode.Orchestrator,
                        "controller" or "finalcontroller" => TerukirdoMode.FinalController,
                        _ => orchestrator.CurrentMode
                    };
                    orchestrator.SetMode(newMode);
                    AnsiConsole.MarkupLine($"[bold green]🎀 [테르키르도] 모드가 '{newMode}'(으)로 전환되었습니다, 주인님.[/]");
                    return string.Empty;

                case "tier":
                    if (parts.Length < 2 || parts[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        orchestrator.SetTier(null);
                        AnsiConsole.MarkupLine("[bold green]🎀 [테르키르도] 적응형 티어가 '자동(Auto Routing)'으로 설정되었습니다.[/]");
                        return string.Empty;
                    }
                    if (int.TryParse(parts[1], out int tierNum) && tierNum >= 0 && tierNum <= 3)
                    {
                        orchestrator.SetTier((AdaptiveLoopTier)tierNum);
                        AnsiConsole.MarkupLine($"[bold green]🎀 [테르키르도] 적응형 티어가 'Tier {tierNum}'(으)로 고정되었습니다, 주인님.[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]사용법: /maid tier <auto | 0 | 1 | 2 | 3>[/]");
                    }
                    return string.Empty;

                case "memory":
                case "sync":
                    await orchestrator.SyncMemoryAsync();
                    AnsiConsole.MarkupLine("[bold green]🎀 [테르키르도] 기술 궤적(docs/Terukirdo_Trajectory.txt) 및 메모리 동기화가 완료되었습니다.[/]");
                    return string.Empty;

                case "tea":
                    AnsiConsole.MarkupLine("[bold pink1]🎀 [테르키르도] 주인님, 따뜻하고 향긋한 얼그레이 홍차를 준비해 드렸습니다. 잠시 쉬어가시며 피로를 푸셔요. ☕🌸[/]");
                    return string.Empty;

                case "status":
                default:
                    var status = await orchestrator.GetStatusAsync();
                    var panel = new Panel(new Markup(
                        $"[bold pink1]👑 1급 메이드 오케스트레이터 테르키르도 현황[/]\n" +
                        $"• [grey]프로토콜:[/] [cyan]{status.ProtocolVersion}[/]\n" +
                        $"• [grey]현재 모드:[/] [bold yellow]{status.CurrentMode}[/]\n" +
                        $"• [grey]적용 티어:[/] [bold green]{status.DefaultTier}[/]\n" +
                        $"• [grey]프라임 디렉티브:[/] [bold green]{(status.PrimeDirectiveActive ? "🛡️ ACTIVE (0 Violations)" : "INACTIVE")}[/]\n" +
                        $"• [grey]워크스페이스:[/] [grey]{status.ActiveWorkspace}[/]\n" +
                        $"• [grey]상태:[/] [italic springgreen2]\"주인님을 성심성의껏 보좌할 준비가 되어 있습니다.\"[/]"
                    ))
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Pink1),
                        Header = new PanelHeader("[bold pink1] 🎀 Terukirdo Maid Orchestrator [/]")
                    };
                    AnsiConsole.Write(panel);
                    return string.Empty;
            }
        }
    }
}
