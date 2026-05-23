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
        private readonly string _localRegistryPath;
        private readonly string _globalRegistryPath;
        private readonly string _workspaceRoot;
        private readonly string _workspaceRootWithSeparator;
        private readonly List<SkillRegistryRecord> _globalSkills = new();
        private readonly List<SkillRegistryRecord> _localSkills = new();
        private readonly SkillRegistryRoot _root = new();

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

            _localRegistryPath = Path.Combine(baseDir, "skill-registry.json");

            string systemBaseDirFull = ResolveFinalPath(AppState.SystemBaseDir);
            string globalDir = Path.Combine(systemBaseDirFull, "skills");
            if (!Directory.Exists(globalDir))
            {
                try { Directory.CreateDirectory(globalDir); } catch { }
            }
            _globalRegistryPath = Path.Combine(systemBaseDirFull, "skill-registry.json");
        }

        /// <summary>
        /// Loads the registry file, or initializes an empty registry when it does not exist.
        /// </summary>
        public async Task LoadAsync()
        {
            _globalSkills.Clear();
            _localSkills.Clear();

            // Load Global Registry
            if (File.Exists(_globalRegistryPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_globalRegistryPath);
                    var globalRoot = JsonSerializer.Deserialize<SkillRegistryRoot>(json);
                    if (globalRoot != null && globalRoot.Skills != null)
                    {
                        foreach (var skill in globalRoot.Skills)
                        {
                            skill.Metadata["IsGlobal"] = "true";
                            _globalSkills.Add(skill);
                        }
                    }
                }
                catch { }
            }

            // Load Local Registry
            if (File.Exists(_localRegistryPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_localRegistryPath);
                    var localRoot = JsonSerializer.Deserialize<SkillRegistryRoot>(json);
                    if (localRoot != null && localRoot.Skills != null)
                    {
                        _localSkills.AddRange(localRoot.Skills);
                    }
                }
                catch { }
            }

            RebuildRootSkills();
        }

        private void RebuildRootSkills()
        {
            _root.Skills.Clear();

            // Local skills override global skills in case of duplicate IDs
            var merged = new Dictionary<string, SkillRegistryRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (var skill in _globalSkills)
            {
                merged[skill.Id] = skill;
            }

            foreach (var skill in _localSkills)
            {
                merged[skill.Id] = skill;
            }

            _root.Skills.AddRange(merged.Values);
        }

        /// <summary>
        /// Saves the current registry state to disk.
        /// </summary>
        public async Task SaveAsync()
        {
            _root.LastUpdatedAt = DateTime.UtcNow;

            // Save Local Registry
            var localRoot = new SkillRegistryRoot
            {
                Skills = _localSkills,
                SchemaVersion = _root.SchemaVersion,
                LastUpdatedAt = _root.LastUpdatedAt
            };
            string localJson = JsonSerializer.Serialize(localRoot, _jsonOptions);
            await File.WriteAllTextAsync(_localRegistryPath, localJson);

            // Save Global Registry (best effort in case base directory is read-only)
            try
            {
                var globalRoot = new SkillRegistryRoot
                {
                    Skills = _globalSkills,
                    SchemaVersion = _root.SchemaVersion,
                    LastUpdatedAt = _root.LastUpdatedAt
                };
                string globalJson = JsonSerializer.Serialize(globalRoot, _jsonOptions);
                await File.WriteAllTextAsync(_globalRegistryPath, globalJson);
            }
            catch { }
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

            // Determine if global or local
            bool isGlobal = false;
            if (record.Metadata.TryGetValue("IsGlobal", out var val) && val.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                isGlobal = true;
            }
            else if (!string.IsNullOrEmpty(record.SourcePath))
            {
                string fullSourcePath = ResolveFinalPath(Path.IsPathRooted(record.SourcePath) ? record.SourcePath : Path.Combine(_workspaceRoot, record.SourcePath));
                string globalSkillsDir = Path.GetFullPath(Path.Combine(AppState.SystemBaseDir, "skills"));
                string globalSkillsDirWithSep = globalSkillsDir.EndsWith(Path.DirectorySeparatorChar) ? globalSkillsDir : globalSkillsDir + Path.DirectorySeparatorChar;

                if (fullSourcePath.StartsWith(globalSkillsDirWithSep, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    isGlobal = true;
                }
            }

            if (isGlobal)
            {
                record.Metadata["IsGlobal"] = "true";
                var existing = _globalSkills.FirstOrDefault(s => s.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _globalSkills.Remove(existing);
                }
                _globalSkills.Add(record);
            }
            else
            {
                var existing = _localSkills.FirstOrDefault(s => s.Id.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    _localSkills.Remove(existing);
                }
                _localSkills.Add(record);
            }

            RebuildRootSkills();
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
            var skill = _root.Skills.FirstOrDefault(s => s.Id.Equals(skillId, StringComparison.OrdinalIgnoreCase));
            if (skill == null) return;

            if (success) skill.Metrics.SuccessCount++;
            else skill.Metrics.FailureCount++;

            if (score > 0)
            {
                int totalCount = skill.Metrics.SuccessCount + skill.Metrics.FailureCount;
                skill.Metrics.AverageScore = ((skill.Metrics.AverageScore * (totalCount - 1)) + score) / totalCount;
            }

            skill.Metrics.LastUsed = DateTime.UtcNow;

            var localSkill = _localSkills.FirstOrDefault(s => s.Id.Equals(skillId, StringComparison.OrdinalIgnoreCase));
            if (localSkill != null)
            {
                localSkill.Metrics = skill.Metrics;
            }
            var globalSkill = _globalSkills.FirstOrDefault(s => s.Id.Equals(skillId, StringComparison.OrdinalIgnoreCase));
            if (globalSkill != null)
            {
                globalSkill.Metrics = skill.Metrics;
            }
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

            string systemBaseDirFull = ResolveFinalPath(AppState.SystemBaseDir);
            string systemSkillsDir = Path.Combine(systemBaseDirFull, "skills");
            string systemSkillsDirWithSeparator = systemSkillsDir.EndsWith(Path.DirectorySeparatorChar)
                ? systemSkillsDir
                : systemSkillsDir + Path.DirectorySeparatorChar;

            bool isUnderSystemSkills = fullPath.Equals(systemSkillsDir, comparison) ||
                                       fullPath.StartsWith(systemSkillsDirWithSeparator, comparison);

            if (!isAtRoot && !isInside && !isUnderSystemSkills)
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
