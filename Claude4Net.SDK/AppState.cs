using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

namespace Claude4Net.SDK
{
    public enum ConnectionStatus { Connecting, Connected, Reconnecting, Disconnected }

    public class ModelUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int? CacheReadInputTokens { get; set; }
        public int? CacheCreationInputTokens { get; set; }
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

    public class TaskStateBase
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public static class AppState
    {
        private static readonly ConcurrentDictionary<string, ModelUsage> _modelUsage = new();
        public static string SessionId { get; private set; } = Guid.NewGuid().ToString();
        public static string OriginalCwd { get; private set; } = Environment.CurrentDirectory;
        public static string CurrentCwd { get; set; } = Environment.CurrentDirectory;
        public static bool IsInteractive { get; set; } = true;
        public static PermissionMode CurrentPermissionMode { get; set; } = PermissionMode.Default;
        public static string ActiveProvider { get; set; } = "gemini";
        public static string ActiveModel { get; set; } = "gemini-3-flash-preview";
        public static ConcurrentDictionary<string, TaskStateBase> Tasks { get; } = new();
        
        public static void AddToTotalCost(double cost, string model, ModelUsage usage)
        {
            _modelUsage.AddOrUpdate(model, usage, (m, old) => new ModelUsage(
                old.InputTokens + usage.InputTokens,
                old.OutputTokens + usage.OutputTokens
            ));
        }
    }
}
