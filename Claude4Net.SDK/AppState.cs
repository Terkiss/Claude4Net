using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 연결 상태를 정의하는 열거형입니다.
    /// </summary>
    public enum ConnectionStatus 
    { 
        /// <summary> 연결 중 </summary>
        Connecting, 
        /// <summary> 연결됨 </summary>
        Connected, 
        /// <summary> 재연결 중 </summary>
        Reconnecting, 
        /// <summary> 연결 끊김 </summary>
        Disconnected 
    }

    /// <summary>
    /// LLM 모델 사용량 및 비용 관련 통계 정보를 담는 클래스입니다.
    /// </summary>
    public class ModelUsage
    {
        /// <summary> 입력 토큰 수 </summary>
        public int InputTokens { get; set; }
        /// <summary> 출력 토큰 수 </summary>
        public int OutputTokens { get; set; }
        /// <summary> 캐시 히트된 입력 토큰 수 </summary>
        public int? CacheReadInputTokens { get; set; }
        /// <summary> 캐시 생성에 사용된 입력 토큰 수 </summary>
        public int? CacheCreationInputTokens { get; set; }
        /// <summary> 웹 검색 요청 횟수 </summary>
        public int? WebSearchRequests { get; set; }

        public ModelUsage(int input, int output, int? cacheRead = 0, int? cacheCreate = 0, int? webSearch = 0)
        {
            InputTokens = input;
            OutputTokens = output;
            CacheReadInputTokens = cacheRead;
            CacheCreationInputTokens = cacheCreate;
            WebSearchRequests = webSearch;
        }
    }

    /// <summary>
    /// 백그라운드 작업 상태를 추적하기 위한 기본 클래스입니다.
    /// </summary>
    public class TaskStateBase
    {
        /// <summary> 작업 고유 ID </summary>
        public string Id { get; set; } = string.Empty;
        /// <summary> 작업 유형 </summary>
        public string Type { get; set; } = string.Empty;
        /// <summary> 현재 상태 (Running, Completed, Failed 등) </summary>
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// 애플리케이션의 전역 상태를 관리하는 정적 클래스입니다.
    /// </summary>
    public static class AppState
    {
        private static readonly ConcurrentDictionary<string, ModelUsage> _modelUsage = new();
        /// <summary> 현재 세션의 고유 ID </summary>
        public static string SessionId { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary> 실행 파일이 위치한 기본 디렉토리 (시스템 리소스 경로) </summary>
        public static string SystemBaseDir { get; set; } = AppDomain.CurrentDomain.BaseDirectory;
        
        /// <summary> 사용자의 현재 작업 디렉토리 </summary>
        public static string? CurrentCwd { get; set; } = null;

        /// <summary> 애플리케이션 시작 시의 원래 작업 디렉토리 </summary>
        public static string OriginalCwd { get; private set; } = Environment.CurrentDirectory;
        /// <summary> 인터랙티브 모드 여부 </summary>
        public static bool IsInteractive { get; set; } = true;
        /// <summary> 현재 권한 모드 </summary>
        public static PermissionMode CurrentPermissionMode { get; set; } = PermissionMode.Default;
        /// <summary> 활성화된 LLM 제공자 이름 </summary>
        public static string ActiveProvider { get; set; } = "gemini";
        /// <summary> 제공자가 명시적으로 설정되었는지 여부 </summary>
        public static bool IsProviderExplicitlySet { get; set; } = false;
        /// <summary> 현재 사용 중인 모델 이름 </summary>
        public static string ActiveModel { get; set; } = "gemini-3-flash-preview";
        /// <summary> 현재 진행 중인 작업 목록 </summary>
        public static ConcurrentDictionary<string, TaskStateBase> Tasks { get; } = new();

        /// <summary> Discord에서 승인 권한을 가진 사용자 ID 목록 </summary>
        public static HashSet<ulong> DiscordAllowedApproverIds { get; } = new();

        /// <summary>
        /// 환경 변수에서 Discord 승인자 목록을 로드합니다.
        /// </summary>
        public static void LoadDiscordApprovers()
        {
            var envValue = Environment.GetEnvironmentVariable("CLAUDE4NET_DISCORD_APPROVER_IDS") ?? 
                           Environment.GetEnvironmentVariable("DISCORD_APPROVER_IDS");

            if (string.IsNullOrEmpty(envValue)) return;

            var ids = envValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var idStr in ids)
            {
                if (ulong.TryParse(idStr.Trim(), out ulong id))
                {
                    DiscordAllowedApproverIds.Add(id);
                }
                else
                {
                    // 파싱 실패 시 로컬에 경고 로그 출력
                    Console.WriteLine($"[Warning] Invalid Discord Approver ID format: {idStr}");
                }
            }
        }

        /// <summary>
        /// 조정(Coordinated) 작업 목록을 반환합니다.
        /// </summary>
        public static IEnumerable<CoordinateTask> GetCoordinatedTasks() => 
            Tasks.Values.OfType<CoordinateTask>();
        
        /// <summary>
        /// 모델별 사용량을 누적 기록합니다.
        /// </summary>
        public static void AddToTotalCost(double cost, string model, ModelUsage usage)
        {
            _modelUsage.AddOrUpdate(model, usage, (m, old) => new ModelUsage(
                old.InputTokens + usage.InputTokens,
                old.OutputTokens + usage.OutputTokens
            ));
        }
    }
}
