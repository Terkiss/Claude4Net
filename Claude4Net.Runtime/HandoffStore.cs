using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// ?몄뀡 ?몃뱶?ㅽ봽 ?뺣낫 諛?利앷굅 ?먮즺瑜???ν븯???대옒?ㅼ엯?덈떎.
    /// </summary>
    public class HandoffStore
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _workspaceRoot;
        private readonly string _sessionId;
        private readonly string _sessionDir;
        private readonly string _evidenceDir;

        public HandoffStore(string workspaceRoot, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new ArgumentNullException(nameof(workspaceRoot));

            // P1 Blocker Fix: Validate sessionId (Same criteria as AgentSessionStore)
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
                throw new ArgumentException("Invalid characters in sessionId.", nameof(sessionId));
            if (Path.IsPathRooted(sessionId))
                throw new ArgumentException("sessionId cannot be a rooted path.", nameof(sessionId));

            _workspaceRoot = Path.GetFullPath(workspaceRoot);
            _sessionId = sessionId;

            string sessionsBaseDir = Path.Combine(_workspaceRoot, ".claude4net", "sessions");
            _sessionDir = Path.Combine(sessionsBaseDir, _sessionId);
            _evidenceDir = Path.Combine(_sessionDir, "evidence");

            // Boundary Check
            string fullSessionsBaseDir = Path.GetFullPath(sessionsBaseDir);
            string fullSessionDir = Path.GetFullPath(_sessionDir);

            if (!fullSessionDir.StartsWith(fullSessionsBaseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Session ID path escape detected.");
        }

        /// <summary>
        /// ?몃뱶?ㅽ봽 ?덉퐫?쒕? ??ν빀?덈떎.
        /// </summary>
        public async Task SaveHandoffAsync(SessionHandoffRecord handoff)
        {
            Directory.CreateDirectory(_sessionDir);
            string filePath = Path.Combine(_sessionDir, "handoff.json");
            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(handoff, _jsonOptions));
        }

        /// <summary>
        /// ?몃뱶?ㅽ봽 ?덉퐫?쒕? 遺덈윭?듬땲??
        /// </summary>
        public async Task<SessionHandoffRecord?> LoadHandoffAsync()
        {
            string filePath = Path.Combine(_sessionDir, "handoff.json");
            if (!File.Exists(filePath)) return null;

            string json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<SessionHandoffRecord>(json);
        }

        /// <summary>
        /// 利앷굅 ?뚯씪????ν빀?덈떎.
        /// </summary>
        public async Task AddEvidenceAsync(string fileName, string content)
        {
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Filename cannot be empty.", nameof(fileName));

            // Security Check: No path traversal, no rooted paths, no slashes
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\") || Path.IsPathRooted(fileName))
            {
                throw new SecurityException($"Invalid evidence filename: {fileName}");
            }

            Directory.CreateDirectory(_evidenceDir);

            string filePath = Path.Combine(_evidenceDir, fileName);
            string fullPath = Path.GetFullPath(filePath);

            string normalizedEvidenceDir = _evidenceDir.EndsWith(Path.DirectorySeparatorChar) ? _evidenceDir : _evidenceDir + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedEvidenceDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Evidence path escape detected.");
            }

            await File.WriteAllTextAsync(fullPath, content);
        }
    }
}
