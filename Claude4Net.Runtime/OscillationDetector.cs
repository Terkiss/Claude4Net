using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class OscillationDetector
    {
        private readonly List<string> _stateHashes = new();
        private const int MAX_HISTORY = 10;

        public bool IsOscillating(IEnumerable<IAgentEvent> events)
        {
            var recentEvents = events.ToList();
            if (recentEvents.Count < 4) return false;

            // Detect repeated ToolResult with same content (Stagnation)
            var toolResults = recentEvents.OfType<ToolResultEvent>().TakeLast(5).ToList();
            if (toolResults.Count >= 3)
            {
                var uniqueResults = toolResults.Select(r => r.Result).Distinct().Count();
                if (uniqueResults == 1) return true;
            }

            // Detect repeated ToolCalled with same ToolName
            var toolCalls = recentEvents.OfType<ToolCalledEvent>().TakeLast(5).ToList();
            if (toolCalls.Count >= 3)
            {
                var uniqueTools = toolCalls.Select(t => t.ToolName).Distinct().Count();
                if (uniqueTools == 1) return true;
            }

            // Detect alternating Thought patterns (simplified)
            var thoughts = recentEvents.OfType<AgentThoughtEvent>().TakeLast(6).ToList();
            if (thoughts.Count >= 4)
            {
                var hashes = thoughts.Select(t => ComputeHash(t.Thought)).ToList();

                // Pattern A-B-A-B
                if (hashes[0] == hashes[2] && hashes[1] == hashes[3]) return true;
            }

            return false;
        }

        private string ComputeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
    }
}
