using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public class SkillResourceManifest
    {
        public string PluginName { get; set; } = string.Empty;
        public string? Checklist { get; set; }
        public string? ErrorPlaybook { get; set; }
        public string? Examples { get; set; }
        public string? ExecutionProtocol { get; set; }
        public DateTime LastLoaded { get; set; }
        public Dictionary<string, DateTime> FileTimestamps { get; set; } = new();

        public bool IsEmpty => 
            string.IsNullOrEmpty(Checklist) && 
            string.IsNullOrEmpty(ErrorPlaybook) && 
            string.IsNullOrEmpty(Examples) && 
            string.IsNullOrEmpty(ExecutionProtocol);
    }
}
