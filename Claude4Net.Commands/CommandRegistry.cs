using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Claude4Net.Commands.Handlers;

namespace Claude4Net.Commands
{
    /// <summary>
    /// 단일 명령어의 정의 및 핸들러를 래핑합니다.
    /// </summary>
    public class Command
    {
        /// <summary> 명령어 이름 (예: help, login) </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 명령어에 대한 설명 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 명령어를 실행할 때 호출되는 비동기 핸들러 </summary>
        public Func<string, IServiceProvider, Task<string>>? Handler { get; set; }
    }

    /// <summary>
    /// Claude4Net 시스템에서 사용 가능한 모든 사용자 명령을 관리하는 레지스트리입니다.
    /// 사용자의 입력 중 '!' 또는 '/'로 시작하는 명령을 가로채어 해당 로직을 실행합니다.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly List<Command> _commands = new()
        {
            // --- [시스템/관리] ---
            new Command { Name = "usage", Description = "Show API token usage, costs, and latency metrics", Handler = SystemCommands.HandleUsage },
            new Command { Name = "help", Description = "Show help", Handler = SystemCommands.HandleHelp },
            new Command { Name = "yolo", Description = "ROOT ACCESS - Bypass all permissions", Handler = SystemCommands.HandleYolo },
            new Command { Name = "doctor", Description = "Run system health check and diagnostics", Handler = SystemCommands.HandleDoctor },
            new Command { Name = "audit", Description = "Show recent security audit logs", Handler = SystemCommands.HandleAudit },
            new Command { Name = "clear", Description = "Clear the console screen", Handler = SystemCommands.HandleClear },
            new Command { Name = "whoami", Description = "Show current user information", Handler = SystemCommands.HandleWhoAmI },
            new Command { Name = "env", Description = "List environment variables (masked, use all/--all for full output)", Handler = SystemCommands.HandleEnv },
            new Command { Name = "status", Description = "Show system and application status", Handler = SystemCommands.HandleStatus },
            new Command { Name = "exit", Description = "Exit the CLI application", Handler = SystemCommands.HandleExit },
            new Command { Name = "plan", Description = "Toggle Plan/Dry-Run mode (Simulate file/state modifications)", Handler = SystemCommands.HandlePlan },

            // --- [프로바이더/모델] ---
            new Command { Name = "login", Description = "Log in to a provider (gemini, claude, glm, ollama, gemini-cli)", Handler = ProviderCommands.HandleLogin },
            new Command { Name = "model", Description = "Browse and change LLM models", Handler = ProviderCommands.HandleModel },
            new Command { Name = "reset", Description = "Reset current conversation history", Handler = ProviderCommands.HandleReset },

            // --- [파일/작업공간] ---
            new Command { Name = "ls", Description = "List files in current directory", Handler = FileCommands.HandleLs },
            new Command { Name = "pwd", Description = "Show current working directory", Handler = FileCommands.HandlePwd },
            new Command { Name = "setworkspace", Description = "Set the root project workspace path (Required for tools)", Handler = FileCommands.HandleSetWorkspace },
            new Command { Name = "cd", Description = "Change current working directory within workspace", Handler = FileCommands.HandleCd },

            // --- [에이전트/목표/조정] ---
            new Command { Name = "goal", Description = "Set autonomous goal (goal <objective> | show | clear) — agent runs until objective is met", Handler = AgentGoalCommands.HandleGoal },
            new Command { Name = "coordinate", Description = "Orchestrate tasks through Planning -> Execution -> Verification phases", Handler = AgentGoalCommands.HandleCoordinate },
            new Command { Name = "routine", Description = "Manage routines (list | show | add | enable | disable | delete | run)", Handler = AgentGoalCommands.HandleRoutine },
            new Command { Name = "handoff", Description = "다른 에이전트에게 세션 인계를 위한 준비 (handoff <status> <summary> [evidenceFiles...])", Handler = AgentGoalCommands.HandleHandoff },
            new Command { Name = "checkpoint", Description = "체크포인트 목록 조회 또는 복구 (list | restore <id>)", Handler = AgentGoalCommands.HandleCheckpoint },

            // --- [스펙/검증/스킬] ---
            new Command { Name = "spec", Description = "Manage specifications (list | new | show | question | answer | criteria | lock | attach)", Handler = SpecVerifyCommands.HandleSpec },
            new Command { Name = "verify", Description = "Run verification checks with default-fail policy and generate machine-readable results", Handler = SpecVerifyCommands.HandleVerify },
            new Command { Name = "skills", Description = "List discovered skills and quality metrics", Handler = SpecVerifyCommands.HandleSkills },
            new Command { Name = "skill-proposals", Description = "List skill evolution proposals", Handler = SpecVerifyCommands.HandleSkillProposals },
            new Command { Name = "skill-propose", Description = "Propose an improvement for a skill", Handler = SpecVerifyCommands.HandleSkillPropose },
            new Command { Name = "skill", Description = "Manage skills and evolution proposals", Handler = SpecVerifyCommands.HandleSkill }
        };

        /// <summary>
        /// 등록된 모든 명령어 목록을 가져옵니다.
        /// </summary>
        public static List<Command> GetCommands() => new(_commands);

        /// <summary>
        /// 등록된 명령어의 개수를 반환합니다.
        /// </summary>
        public static int GetCommandCount() => _commands.Count;

        /// <summary>
        /// 명령어 이름으로 특정 명령어를 검색합니다. (접두사 '!' 또는 '/' 제외 후 비교)
        /// </summary>
        public static Command? FindCommand(string name) => _commands.Find(c => c.Name.Equals(name.TrimStart('!', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
