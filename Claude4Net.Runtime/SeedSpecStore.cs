using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SeedSpecStore
    {
        private readonly string _workspaceRoot;

        public SeedSpecStore(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        private string GetSpecDir(string specId)
        {
            return Path.Combine(_workspaceRoot, ".claude4net", "specs", specId);
        }

        private string GetSpecFilePath(string specId)
        {
            return Path.Combine(GetSpecDir(specId), "seed-spec.json");
        }

        public async Task<SeedSpecRecord?> LoadAsync(string specId)
        {
            var path = GetSpecFilePath(specId);
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<SeedSpecRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task SaveAsync(SeedSpecRecord spec)
        {
            var dir = GetSpecDir(spec.Id);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            spec.UpdatedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(GetSpecFilePath(spec.Id), json);
        }

        public IEnumerable<SeedSpecRecord> ListSpecs()
        {
            var specsDir = Path.Combine(_workspaceRoot, ".claude4net", "specs");
            if (!Directory.Exists(specsDir)) yield break;

            foreach (var dir in Directory.GetDirectories(specsDir))
            {
                var file = Path.Combine(dir, "seed-spec.json");
                if (File.Exists(file))
                {
                    SeedSpecRecord? spec = null;
                    try
                    {
                        var json = File.ReadAllText(file);
                        spec = JsonSerializer.Deserialize<SeedSpecRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { }
                    if (spec != null) yield return spec;
                }
            }
        }
    }
}
