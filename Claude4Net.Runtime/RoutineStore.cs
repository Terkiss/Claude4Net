using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class RoutineStore
    {
        private readonly string _workspaceRoot;

        public RoutineStore(string workspaceRoot)
        {
            _workspaceRoot = workspaceRoot;
        }

        private string GetRoutineFilePath(string routineId)
        {
            if (string.IsNullOrWhiteSpace(routineId) ||
                routineId.Contains("..") || routineId.Contains('/') || routineId.Contains('\\') || routineId.Contains(':') ||
                routineId.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
            {
                throw new ArgumentException("Invalid routine ID: path traversal or illegal characters detected.", nameof(routineId));
            }
            return Path.Combine(_workspaceRoot, ".claude4net", "routines", $"{routineId}.json");
        }

        private string GetRoutineRunDir(string routineId)
        {
            if (string.IsNullOrWhiteSpace(routineId) ||
                routineId.Contains("..") || routineId.Contains('/') || routineId.Contains('\\') || routineId.Contains(':') ||
                routineId.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
            {
                throw new ArgumentException("Invalid routine ID: path traversal or illegal characters detected.", nameof(routineId));
            }
            return Path.Combine(_workspaceRoot, ".claude4net", "routine-runs", routineId);
        }

        public async Task<RoutineDefinition?> LoadAsync(string routineId)
        {
            var path = GetRoutineFilePath(routineId);
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<RoutineDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task SaveAsync(RoutineDefinition routine)
        {
            var path = GetRoutineFilePath(routine.Id);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            routine.UpdatedAt = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(routine, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task DeleteAsync(string routineId)
        {
            var path = GetRoutineFilePath(routineId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            await Task.CompletedTask;
        }

        public IEnumerable<RoutineDefinition> ListRoutines()
        {
            var dir = Path.Combine(_workspaceRoot, ".claude4net", "routines");
            if (!Directory.Exists(dir)) yield break;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                RoutineDefinition? routine = null;
                try
                {
                    var json = File.ReadAllText(file);
                    routine = JsonSerializer.Deserialize<RoutineDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
                if (routine != null) yield return routine;
            }
        }

        public async Task SaveRunRecordAsync(RoutineRunRecord record)
        {
            var dir = GetRoutineRunDir(record.RoutineId);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var path = Path.Combine(dir, $"{record.RunId}.json");
            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public IEnumerable<RoutineRunRecord> GetRunRecords(string routineId)
        {
            var dir = GetRoutineRunDir(routineId);
            if (!Directory.Exists(dir)) yield break;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                RoutineRunRecord? record = null;
                try
                {
                    var json = File.ReadAllText(file);
                    record = JsonSerializer.Deserialize<RoutineRunRecord>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch { }
                if (record != null) yield return record;
            }
        }
    }
}
