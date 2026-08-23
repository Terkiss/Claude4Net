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
            new Command { Name = "usage", Description = "API 토큰 소모량, 비용 및 실시간 컨텍스트 윈도우 잔여량 확인", Handler = SystemCommands.HandleUsage },
            new Command { Name = "api", Description = "인프로세스 OpenAI 호환 API 서버 제어 (api on [port] | off | status)", Handler = SystemCommands.HandleApi },
            new Command { Name = "help", Description = "도움말 및 CLI 사용 가이드 표시", Handler = SystemCommands.HandleHelp },
            new Command { Name = "yolo", Description = "루트 권한 - 모든 보안 결재 및 권한 검사 우회 (주의 요망)", Handler = SystemCommands.HandleYolo },
            new Command { Name = "doctor", Description = "시스템 의존성, 프로바이더 및 환경 상태 종합 진단", Handler = SystemCommands.HandleDoctor },
            new Command { Name = "audit", Description = "최근 보안 감사 및 도구 실행 로그 조회", Handler = SystemCommands.HandleAudit },
            new Command { Name = "clear", Description = "터미널 콘솔 화면 지우기", Handler = SystemCommands.HandleClear },
            new Command { Name = "whoami", Description = "현재 실행 사용자 및 호스트 시스템 정보 확인", Handler = SystemCommands.HandleWhoAmI },
            new Command { Name = "env", Description = "환경 변수 목록 조회 (민감값 마스킹, /env all 로 전체 조회)", Handler = SystemCommands.HandleEnv },
            new Command { Name = "status", Description = "시스템 리소스, 앱 런타임 및 CQRS 세션 프로젝션 상태", Handler = SystemCommands.HandleStatus },
            new Command { Name = "exit", Description = "CLI 애플리케이션 안전 종료", Handler = SystemCommands.HandleExit },
            new Command { Name = "plan", Description = "Plan/Dry-Run 시뮬레이션 모드 토글 (파일 및 상태 변경 사전 시뮬레이션)", Handler = SystemCommands.HandlePlan },

            // --- [프로바이더/모델] ---
            new Command { Name = "login", Description = "프로바이더 로그인 및 API 키 등록 (qwen, alibaba, gemini, claude, glm, ollama 등)", Handler = ProviderCommands.HandleLogin },
            new Command { Name = "model", Description = "LLM 모델 목록 탐색 및 활성 모델 전환", Handler = ProviderCommands.HandleModel },

            // --- [파일/작업공간] ---
            new Command { Name = "ls", Description = "현재 작업 디렉토리의 파일 및 폴더 목록 조회", Handler = FileCommands.HandleLs },
            new Command { Name = "pwd", Description = "현재 작업 디렉토리 절대 경로 확인", Handler = FileCommands.HandlePwd },
            new Command { Name = "setworkspace", Description = "에이전트 도구 실행을 위한 프로젝트 루트 작업 공간 지정", Handler = FileCommands.HandleSetWorkspace },
            new Command { Name = "cd", Description = "설정된 작업 공간 내에서 하위 디렉토리 이동", Handler = FileCommands.HandleCd },

            // --- [에이전트/목표/조정] ---
            new Command { Name = "goal", Description = "자율 목표 에이전트 실행 (goal <목표> | show | clear) — 목표 달성 시까지 자율 수행", Handler = AgentGoalCommands.HandleGoal },
            new Command { Name = "coordinate", Description = "태스크 기획(Planning) -> 실행(Execution) -> 검증(Verification) 3단계 조정", Handler = AgentGoalCommands.HandleCoordinate },
            new Command { Name = "routine", Description = "주기적 및 이벤트 기반 루틴 관리 (list | show | add | enable | disable | delete | run)", Handler = AgentGoalCommands.HandleRoutine },
            new Command { Name = "handoff", Description = "다른 에이전트/세션 인계를 위한 핸드오프 기록 생성 및 저장", Handler = AgentGoalCommands.HandleHandoff },
            new Command { Name = "checkpoint", Description = "체크포인트 스냅샷 목록 조회 및 복원 (list | restore <id>)", Handler = AgentGoalCommands.HandleCheckpoint },

            // --- [스펙/검증/스킬] ---
            new Command { Name = "spec", Description = "SeedSpec 요구사항 및 수락 기준 관리 (list | new | show | question | answer | criteria | lock | attach)", Handler = SpecVerifyCommands.HandleSpec },
            new Command { Name = "verify", Description = "기본 실패(Default-fail) 정책 기반 빌드 및 단위 테스트 무결성 검증", Handler = SpecVerifyCommands.HandleVerify },
            new Command { Name = "skill", Description = "에이전트 스킬 및 진화 제안 관리 (analyze | proposals | propose | validate | approve | reject | apply)", Handler = SpecVerifyCommands.HandleSkill },

            // --- [테르키르도 오케스트레이터] ---
            new Command { Name = "maid", Description = "1급 메이드 오케스트레이터 테르키르도 관제 (maid status | mode | tier | memory | tea)", Handler = TerukirdoCommands.HandleMaid },
            new Command { Name = "terukirdo", Description = "/maid 명령어의 별칭", Handler = TerukirdoCommands.HandleMaid }
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
