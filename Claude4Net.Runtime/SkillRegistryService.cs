using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// Manages and resolves the skill registry.
    /// Handles file-backed persistence, skill discovery, and quality tracking.
    /// </summary>
    public class SkillRegistryService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _registryPath;
        private readonly string _workspaceRoot;
        private readonly string _workspaceRootWithSeparator;
        private SkillRegistryRoot _root = new();

        public SkillRegistryService(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new ArgumentException("Workspace root is required for SkillRegistryService.");

            _workspaceRoot = Path.GetFullPath(workspaceRoot);
            _workspaceRootWithSeparator = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
                ? _workspaceRoot
                : _workspaceRoot + Path.DirectorySeparatorChar;

            string baseDir = Path.Combine(_workspaceRoot, ".claude4net");
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

            _registryPath = Path.Combine(baseDir, "skill-registry.json");
        }

        /// <summary>
        /// Loads the registry file, or initializes an empty registry when it does not exist.
        /// </summary>
        public async Task LoadAsync()
        {
            if (File.Exists(_registryPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_registryPath);
                    _root = JsonSerializer.Deserialize<SkillRegistryRoot>(json) ?? new SkillRegistryRoot();
                }
                catch
                {
                    _root = new SkillRegistryRoot();
                }
            }
            else
            {
                _root = new SkillRegistryRoot();
            }
        }

        /// <summary>
        /// Saves the current registry state to disk.
        /// </summary>
        public async Task SaveAsync()
        {
            _root.LastUpdatedAt = DateTime.UtcNow;
            string json = JsonSerializer.Serialize(_root, _jsonOptions);
            await File.WriteAllTextAsync(_registryPath, json);
        }

        /// <summary>
        /// Registers a new skill or updates an existing skill record.
        /// </summary>
        public void RegisterSkill(SkillRegistryRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.Id)) throw new ArgumentException("Skill ID is required.");

            if (!string.IsNullOrEmpty(record.SourcePath))
            {
                ValidatePath(record.SourcePath);
            }

            var existing = _root.Skills.FirstOrDefault(s => s.Id == record.Id);
            if (existing != null)
            {
                _root.Skills.Remove(existing);
            }
            _root.Skills.Add(record);
        }

        /// <summary>
        /// Resolves a skill by ID, display name, or alias.
        /// </summary>
        public SkillRegistryRecord? ResolveSkill(string identity)
        {
            return _root.Skills.FirstOrDefault(s =>
                s.Id.Equals(identity, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Equals(identity, StringComparison.OrdinalIgnoreCase) ||
                s.Aliases.Any(a => a.Equals(identity, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Updates skill quality metrics.
        /// </summary>
        public void UpdateMetrics(string skillId, bool success, double score = 0)
        {
            var skill = _root.Skills.FirstOrDefault(s => s.Id == skillId);
            if (skill == null) return;

            if (success) skill.Metrics.SuccessCount++;
            else skill.Metrics.FailureCount++;

            if (score > 0)
            {
                int totalCount = skill.Metrics.SuccessCount + skill.Metrics.FailureCount;
                skill.Metrics.AverageScore = ((skill.Metrics.AverageScore * (totalCount - 1)) + score) / totalCount;
            }

            skill.Metrics.LastUsed = DateTime.UtcNow;
        }

        /// <summary>
        /// Returns all registered skills.
        /// </summary>
        public IReadOnlyList<SkillRegistryRecord> ListSkills() => _root.Skills.AsReadOnly();

        /// <summary>
        /// Reads a sidecar skill ID file next to the skill source file when present.
        /// </summary>
        public string? GetIdFromSidecar(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;

            try
            {
                ValidatePath(sourcePath);
                string sidecarPath = sourcePath + ".skill_id";
                ValidatePath(sidecarPath);

                string fullSidecarPath = ResolveFinalPath(Path.IsPathRooted(sidecarPath) ? sidecarPath : Path.Combine(_workspaceRoot, sidecarPath));

                if (File.Exists(fullSidecarPath))
                {
                    return File.ReadAllText(fullSidecarPath).Trim();
                }
            }
            catch
            {
                // Fail closed.
            }
            return null;
        }

        private void ValidatePath(string path)
        {
            string fullPath = ResolveFinalPath(Path.IsPathRooted(path) ? path : Path.Combine(_workspaceRoot, path));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            bool isAtRoot = fullPath.Equals(_workspaceRoot, comparison);
            bool isInside = fullPath.StartsWith(_workspaceRootWithSeparator, comparison);

            if (!isAtRoot && !isInside)
                throw new UnauthorizedAccessException($"Path '{path}' is outside safe boundaries.");
        }

        /// <summary>
        /// Resolves the final path by walking each path segment and resolving symlinks or reparse points.
        /// </summary>
        public string ResolveFinalPath(string path)
        {
            return ResolveFinalPathInternal(path, p =>
            {
                if (File.Exists(p))
                {
                    var info = new FileInfo(p);
                    if (info.LinkTarget != null) return info.ResolveLinkTarget(true)?.FullName;
                }
                else if (Directory.Exists(p))
                {
                    var info = new DirectoryInfo(p);
                    if (info.LinkTarget != null) return info.ResolveLinkTarget(true)?.FullName;
                }
                return null;
            });
        }

        /// <summary>
        /// Testable core path resolution helper.
        /// </summary>
        internal static string ResolveFinalPathInternal(string path, Func<string, string?> linkResolver)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                string remaining = fullPath.Substring(root.Length);
                string[] segments = remaining.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

                string current = root;

                foreach (var segment in segments)
                {
                    current = Path.Combine(current, segment);
                    string? resolved = linkResolver(current);
                    if (resolved != null)
                    {
                        current = resolved;
                    }
                }

                return Path.GetFullPath(current);
            }
            catch
            {
                return Path.GetFullPath(path);
            }
        }
    }
}
