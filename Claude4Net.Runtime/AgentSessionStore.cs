using System;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 에이전트 세션의 파일 기반 영속성 저장을 담당하는 클래스입니다.
    /// .claude4net/sessions/{sessionId}/ 구조를 관리합니다.
    /// </summary>
    public class AgentSessionStore
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _workspaceRoot;
        private readonly string _sessionBaseDir;

        public string WorkspaceRoot => _workspaceRoot;
        public string SessionId { get; }
        public string SessionDir { get; }

        public AgentSessionStore(string workspaceRoot, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new ArgumentException("Workspace root cannot be null or empty.", nameof(workspaceRoot));

            ValidateName(sessionId, nameof(sessionId));

            SessionId = sessionId;
            _workspaceRoot = workspaceRoot;
            _sessionBaseDir = Path.Combine(_workspaceRoot, ".claude4net", "sessions");

            // P1 Blocker Fix: Path Traversal Defense with FullPath check
            string targetDir = Path.Combine(_sessionBaseDir, sessionId);
            string fullTargetDir = Path.GetFullPath(targetDir);
            string fullBaseDir = Path.GetFullPath(_sessionBaseDir);

            if (!fullTargetDir.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Session directory escape detected.");

            SessionDir = fullTargetDir;
        }

        private static void ValidateName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", paramName);

            // P1 Blocker Fix: Path Traversal Defense (Block dangerous characters)
            if (name.Contains("..") || name.Contains("/") || name.Contains("\\") || name.Contains(":"))
                throw new ArgumentException($"Invalid characters in {paramName}.", paramName);

            if (Path.IsPathRooted(name))
                throw new ArgumentException($"{paramName} cannot be a rooted path.", paramName);
        }

        /// <summary>
        /// 세션 디렉토리를 초기화하고 메타데이터를 저장합니다.
        /// </summary>
        public async Task InitializeAsync(AgentSessionRecord record)
        {
            EnsureSessionDirectory();

            string sessionFilePath = Path.Combine(SessionDir, "session.json");
            string json = JsonSerializer.Serialize(record, _jsonOptions);
            await File.WriteAllTextAsync(sessionFilePath, json);
        }

        /// <summary>
        /// 태스크 보드 상태를 저장합니다.
        /// </summary>
        public async Task SaveTaskBoardAsync(AgentTaskBoardRecord board)
        {
            EnsureSessionDirectory();
            string filePath = Path.Combine(SessionDir, "task-board.json");
            string json = JsonSerializer.Serialize(board, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// 태스크 보드 상태를 불러옵니다.
        /// </summary>
        public async Task<AgentTaskBoardRecord?> LoadTaskBoardAsync()
        {
            string filePath = Path.Combine(SessionDir, "task-board.json");
            if (!File.Exists(filePath)) return null;

            string json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<AgentTaskBoardRecord>(json);
        }

        /// <summary>
        /// 에이전트의 진행 상황을 JSONL 파일에 추가합니다.
        /// </summary>
        public async Task AppendProgressAsync(string agentName, AgentProgressEvent progressEvent)
        {
            ValidateName(agentName, nameof(agentName));

            EnsureSessionDirectory();
            string fileName = $"progress-{agentName}.jsonl";
            string filePath = Path.Combine(SessionDir, fileName);

            // JSONL: 한 줄에 하나의 JSON 객체 (Indented=false)
            string jsonLine = JsonSerializer.Serialize(progressEvent) + Environment.NewLine;
            await File.AppendAllTextAsync(filePath, jsonLine);
        }

        /// <summary>
        /// 에이전트의 최종 결과를 마크다운 파일로 저장합니다.
        /// </summary>
        public async Task SaveResultAsync(string agentName, string markdown)
        {
            ValidateName(agentName, nameof(agentName));

            EnsureSessionDirectory();
            string fileName = $"result-{agentName}.md";
            string filePath = Path.Combine(SessionDir, fileName);
            await File.WriteAllTextAsync(filePath, markdown);
        }

        /// <summary>
        /// 세션 메타데이터를 불러옵니다. (resume 용도)
        /// </summary>
        public static async Task<AgentSessionRecord?> LoadSessionRecordAsync(string workspaceRoot, string sessionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(workspaceRoot)) return null;
                ValidateName(sessionId, nameof(sessionId));

                string sessionBaseDir = Path.Combine(workspaceRoot, ".claude4net", "sessions");
                string sessionFilePath = Path.Combine(sessionBaseDir, sessionId, "session.json");

                string fullPath = Path.GetFullPath(sessionFilePath);
                string fullBaseDir = Path.GetFullPath(sessionBaseDir);

                if (!fullPath.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
                    return null; // Security escape detected

                if (!File.Exists(fullPath)) return null;

                string json = await File.ReadAllTextAsync(fullPath);
                return JsonSerializer.Deserialize<AgentSessionRecord>(json);
            }
            catch
            {
                return null; // Fail-closed
            }
        }

        private void EnsureSessionDirectory()
        {
            if (!Directory.Exists(SessionDir))
            {
                Directory.CreateDirectory(SessionDir);
            }
        }
    }
}
