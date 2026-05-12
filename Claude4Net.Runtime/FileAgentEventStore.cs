using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class FileAgentEventStore : IAgentEventStore
    {
        private readonly string _workspaceRoot;
        private static readonly JsonSerializerOptions _options = new() { WriteIndented = false };

        public FileAgentEventStore(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        private string GetSessionDir(string sessionId)
        {
            return Path.Combine(_workspaceRoot, ".claude4net", "sessions", sessionId);
        }

        private string GetEventsPath(string sessionId) => Path.Combine(GetSessionDir(sessionId), "events.jsonl");
        private string GetSnapshotPath(string sessionId) => Path.Combine(GetSessionDir(sessionId), "snapshot.json");

        public async Task AppendEventAsync(string sessionId, IAgentEvent @event)
        {
            var dir = GetSessionDir(sessionId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var path = GetEventsPath(sessionId);

            var wrapper = new EventEnvelope
            {
                Type = @event.EventType,
                Payload = JsonSerializer.SerializeToElement(@event, @event.GetType(), _options)
            };

            string line = JsonSerializer.Serialize(wrapper, _options) + Environment.NewLine;
            await File.AppendAllTextAsync(path, line);
        }

        public async Task<IEnumerable<IAgentEvent>> GetEventsAsync(string sessionId, long afterVersion = 0)
        {
            var path = GetEventsPath(sessionId);
            if (!File.Exists(path)) return Enumerable.Empty<IAgentEvent>();

            var events = new List<IAgentEvent>();
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var envelope = JsonSerializer.Deserialize<EventEnvelope>(line);
                if (envelope == null) continue;

                IAgentEvent? ev = envelope.Type switch
                {
                    "SessionStarted" => JsonSerializer.Deserialize<SessionStartedEvent>(envelope.Payload.GetRawText()),
                    "UserPromptReceived" => JsonSerializer.Deserialize<UserPromptReceivedEvent>(envelope.Payload.GetRawText()),
                    "AgentThought" => JsonSerializer.Deserialize<AgentThoughtEvent>(envelope.Payload.GetRawText()),
                    "ToolCalled" => JsonSerializer.Deserialize<ToolCalledEvent>(envelope.Payload.GetRawText()),
                    "ToolResult" => JsonSerializer.Deserialize<ToolResultEvent>(envelope.Payload.GetRawText()),
                    "FinalResponseGenerated" => JsonSerializer.Deserialize<FinalResponseGeneratedEvent>(envelope.Payload.GetRawText()),
                    "StateTransition" => JsonSerializer.Deserialize<StateTransitionEvent>(envelope.Payload.GetRawText()),
                    "TaskAttemptStarted" => JsonSerializer.Deserialize<TaskAttemptStartedEvent>(envelope.Payload.GetRawText()),
                    "TaskAttemptCompleted" => JsonSerializer.Deserialize<TaskAttemptCompletedEvent>(envelope.Payload.GetRawText()),
                    "VerificationCompleted" => JsonSerializer.Deserialize<VerificationCompletedEvent>(envelope.Payload.GetRawText()),
                    _ => null
                };

                if (ev != null && ev.Version > afterVersion)
                {
                    events.Add(ev);
                }
            }

            return events;
        }

        public async Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot)
        {
            var dir = GetSessionDir(sessionId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var path = GetSnapshotPath(sessionId);
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task<AgentStateSnapshot?> GetLatestSnapshotAsync(string sessionId)
        {
            var path = GetSnapshotPath(sessionId);
            if (!File.Exists(path)) return null;

            string json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AgentStateSnapshot>(json);
        }

        // --- K034: Event Store v2 쿼리 메서드 ---

        /// <summary>
        /// 세션의 총 이벤트 수를 반환합니다.
        /// </summary>
        public async Task<int> GetEventCountAsync(string sessionId)
        {
            var path = GetEventsPath(sessionId);
            if (!File.Exists(path)) return 0;

            int count = 0;
            using var reader = new StreamReader(path);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) count++;
            }
            return count;
        }

        /// <summary>
        /// 시간 범위 내의 이벤트만 필터링하여 반환합니다.
        /// </summary>
        public async Task<IEnumerable<IAgentEvent>> GetEventsByTimeRangeAsync(
            string sessionId, DateTime from, DateTime to)
        {
            var allEvents = await GetEventsAsync(sessionId, 0);
            return allEvents.Where(e => e.Timestamp >= from && e.Timestamp <= to);
        }

        /// <summary>
        /// 특정 이벤트 타입만 필터링하여 반환합니다.
        /// </summary>
        public async Task<IEnumerable<T>> GetEventsByTypeAsync<T>(string sessionId) where T : IAgentEvent
        {
            var allEvents = await GetEventsAsync(sessionId, 0);
            return allEvents.OfType<T>();
        }

        private class EventEnvelope
        {
            public string Type { get; set; } = string.Empty;
            public JsonElement Payload { get; set; }
        }
    }
}
