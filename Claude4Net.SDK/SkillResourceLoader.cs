using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Claude4Net.SDK
{
    public class SkillResourceLoader
    {
        private static readonly ConcurrentDictionary<string, SkillResourceManifest> _cache = new();
        private readonly string _baseResourcesDir;

        public SkillResourceLoader(string? baseDir = null)
        {
            _baseResourcesDir = baseDir ?? Path.Combine(AppState.SystemBaseDir, ".resources");
        }

        public SkillResourceManifest LoadForPlugin(string pluginName)
        {
            string pluginDir = Path.Combine(_baseResourcesDir, pluginName);
            
            if (_cache.TryGetValue(pluginName, out var cached))
            {
                if (!IsCacheStale(pluginDir, cached))
                {
                    return cached;
                }
            }

            var manifest = new SkillResourceManifest { PluginName = pluginName };
            
            if (Directory.Exists(pluginDir))
            {
                manifest.Checklist = TryReadFile(Path.Combine(pluginDir, "checklist.md"), manifest);
                manifest.ErrorPlaybook = TryReadFile(Path.Combine(pluginDir, "error-playbook.md"), manifest);
                manifest.Examples = TryReadFile(Path.Combine(pluginDir, "examples.md"), manifest);
                manifest.ExecutionProtocol = TryReadFile(Path.Combine(pluginDir, "execution-protocol.md"), manifest);
            }

            manifest.LastLoaded = DateTime.Now;
            _cache[pluginName] = manifest;
            return manifest;
        }

        private bool IsCacheStale(string pluginDir, SkillResourceManifest cached)
        {
            if (!Directory.Exists(pluginDir)) return !cached.IsEmpty;

            foreach (var kvp in cached.FileTimestamps)
            {
                if (File.Exists(kvp.Key))
                {
                    if (File.GetLastWriteTime(kvp.Key) > kvp.Value) return true;
                }
                else
                {
                    // File deleted
                    return true;
                }
            }

            // Check if new files appeared
            string[] expectedFiles = { "checklist.md", "error-playbook.md", "examples.md", "execution-protocol.md" };
            foreach (var file in expectedFiles)
            {
                string fullPath = Path.Combine(pluginDir, file);
                if (File.Exists(fullPath) && !cached.FileTimestamps.ContainsKey(fullPath)) return true;
            }

            return false;
        }

        private string? TryReadFile(string path, SkillResourceManifest manifest)
        {
            if (File.Exists(path))
            {
                try
                {
                    manifest.FileTimestamps[path] = File.GetLastWriteTime(path);
                    return File.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public void ClearCache()
        {
            _cache.Clear();
        }
    }
}
