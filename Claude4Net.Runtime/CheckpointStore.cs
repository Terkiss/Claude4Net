using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 泥댄???????곗씠??? ???ν????????ы븯?????????엯???떎.
    /// .claude4net/sessions/{sessionId}/checkpoints/ ???쐞???곗씠??? ??????땲??
    /// </summary>
    public class CheckpointStore
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _workspaceRoot;
        private readonly string _sessionId;
        private readonly string _checkpointsDir;

        public CheckpointStore(string workspaceRoot, string sessionId)
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
            _checkpointsDir = Path.Combine(sessionsBaseDir, _sessionId, "checkpoints");

            // Boundary Check
            string fullSessionsBaseDir = Path.GetFullPath(sessionsBaseDir);
            string fullCheckpointsDir = Path.GetFullPath(_checkpointsDir);

            if (!fullCheckpointsDir.StartsWith(fullSessionsBaseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Session ID path escape detected.");
        }

        private void ValidateCheckpointId(string checkpointId)
        {
            if (string.IsNullOrWhiteSpace(checkpointId)) throw new ArgumentException("Checkpoint ID cannot be empty.", nameof(checkpointId));

            // alphanumeric, hyphens, underscores only
            if (!Regex.IsMatch(checkpointId, @"^[a-zA-Z0-9\-_]+$"))
                throw new SecurityException($"Invalid checkpoint ID format: {checkpointId}");

            if (checkpointId.Contains("..") || checkpointId.Contains("/") || checkpointId.Contains("\\"))
                throw new SecurityException("Potential path traversal in checkpoint ID.");
        }

        private string GetSafeCheckpointDir(string checkpointId)
        {
            ValidateCheckpointId(checkpointId);

            string combined = Path.Combine(_checkpointsDir, checkpointId);
            string fullPath = Path.GetFullPath(combined);

            string normalizedCheckpointsDir = _checkpointsDir.EndsWith(Path.DirectorySeparatorChar) ? _checkpointsDir : _checkpointsDir + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedCheckpointsDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Checkpoint path escape detected.");

            return fullPath;
        }

        /// <summary>
        /// 새로운 체크포인트를 생성하고 변경된 파일 및 메모리 상태를 백업합니다.
        /// </summary>
        public async Task<string> CreateCheckpointAsync(string toolCallId, string toolName, List<string> files, string? description = null, bool includeMemoryState = false)
        {
            string? stateSnapshotId = null;
            if (includeMemoryState)
            {
                var ctx = new WorkspaceStateContext { WorkspaceRoot = _workspaceRoot, SessionId = _sessionId };
                var store = PandasUniverseManager.Instance.GetStore(ctx);
                store.ForceSaveSync();
                stateSnapshotId = await store.CreateSnapshotAsync(ctx, $"checkpoint_{toolName}");

                if (!files.Contains(ctx.MemoryDbPath))
                {
                    files = new List<string>(files) { ctx.MemoryDbPath };
                }
            }
            string checkpointId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 8);
            string dir = Path.Combine(_checkpointsDir, checkpointId);
            Directory.CreateDirectory(dir);

            string beforeDir = Path.Combine(dir, "before");
            Directory.CreateDirectory(beforeDir);

            var relativeFiles = new List<string>();
            foreach (var file in files)
            {
                string relativePath = GetRelativePath(file);
                relativeFiles.Add(relativePath);

                string fullPath = Path.Combine(_workspaceRoot, relativePath);

                if (File.Exists(fullPath))
                {
                    string backupPath = Path.Combine(beforeDir, relativePath);
                    string? backupDir = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrEmpty(backupDir)) Directory.CreateDirectory(backupDir);

                    File.Copy(fullPath, backupPath, true);
                }
            }

            var manifest = new CheckpointManifest
            {
                Id = checkpointId,
                ToolCallId = toolCallId,
                ToolName = toolName,
                Description = description,
                ChangedFiles = relativeFiles,
                CreatedAt = DateTime.UtcNow,
                Provider = AppState.ActiveProvider,
                Model = AppState.ActiveModel,
                StateSnapshotId = stateSnapshotId,
                IncludesMemoryState = includeMemoryState
            };

            string manifestPath = Path.Combine(dir, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions));

            return checkpointId;
        }

        /// <summary>
        /// 泥댄?????몄뿉 蹂€寃쎈?????튂(diff)?????ν빀???떎.
        /// </summary>
        public async Task SaveDiffAsync(string checkpointId, string diff)
        {
            string dir = GetSafeCheckpointDir(checkpointId);
            if (!Directory.Exists(dir)) return;

            string filePath = Path.Combine(dir, "after.patch");
            await File.WriteAllTextAsync(filePath, diff);
        }

        /// <summary>
        /// 紐⑤??泥댄??????紐⑸???諛섑????땲??
        /// </summary>
        public async Task<List<CheckpointManifest>> ListCheckpointsAsync()
        {
            if (!Directory.Exists(_checkpointsDir)) return new List<CheckpointManifest>();

            var manifests = new List<CheckpointManifest>();
            var dirs = Directory.GetDirectories(_checkpointsDir);

            foreach (var dir in dirs)
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string json = await File.ReadAllTextAsync(manifestPath);
                        var manifest = JsonSerializer.Deserialize<CheckpointManifest>(json);
                        if (manifest != null) manifests.Add(manifest);
                    }
                    catch { }
                }
            }

            return manifests.OrderByDescending(m => m.CreatedAt).ToList();
        }

        /// <summary>
        /// 체크포인트의 'before' 상태로 파일 및 메모리 상태를 복구합니다.
        /// </summary>
        public async Task RestoreCheckpointAsync(string checkpointId)
        {
            string dir = GetSafeCheckpointDir(checkpointId);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"Checkpoint {checkpointId} not found.");

            string manifestPath = Path.Combine(dir, "manifest.json");
            CheckpointManifest? manifest = null;
            if (File.Exists(manifestPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(manifestPath);
                    manifest = JsonSerializer.Deserialize<CheckpointManifest>(json);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to deserialize checkpoint manifest for {checkpointId}: {ex.Message}", ex);
                }
            }

            // Restore memory state if checkpoint contains a snapshot reference
            if (manifest != null && manifest.IncludesMemoryState && !string.IsNullOrEmpty(manifest.StateSnapshotId))
            {
                var ctx = new WorkspaceStateContext { WorkspaceRoot = _workspaceRoot, SessionId = _sessionId };
                var store = PandasUniverseManager.Instance.GetStore(ctx);
                try
                {
                    await store.RestoreSnapshotAsync(ctx, manifest.StateSnapshotId);
                }
                catch (FileNotFoundException fnfEx)
                {
                    throw new InvalidOperationException($"Memory snapshot file for snapshot ID '{manifest.StateSnapshotId}' is missing or has been deleted.", fnfEx);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Memory snapshot data for snapshot ID '{manifest.StateSnapshotId}' is corrupted or failed to load: {ex.Message}", ex);
                }
            }

            string beforeDir = Path.Combine(dir, "before");
            if (!Directory.Exists(beforeDir)) return;

            bool memoryRestored = false;
            var files = Directory.GetFiles(beforeDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string relativePath = Path.GetRelativePath(beforeDir, file);
                string targetPath = Path.Combine(_workspaceRoot, relativePath);

                // Ensure targetPath is within workspaceRoot
                string fullTargetPath = Path.GetFullPath(targetPath);
                string normalizedWorkspaceRoot = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar) ? _workspaceRoot : _workspaceRoot + Path.DirectorySeparatorChar;
                if (!fullTargetPath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase) && fullTargetPath != _workspaceRoot)
                    throw new SecurityException("Restore path escape detected.");

                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                File.Copy(file, targetPath, true);
                if (targetPath.EndsWith("memory.db", StringComparison.OrdinalIgnoreCase)) memoryRestored = true;
            }

            // If we restored a legacy checkpoint (without snapshot but had memory.db in backup)
            if (memoryRestored && (manifest == null || !manifest.IncludesMemoryState || string.IsNullOrEmpty(manifest.StateSnapshotId)))
            {
                var ctx = new WorkspaceStateContext { WorkspaceRoot = _workspaceRoot, SessionId = _sessionId };
                await PandasUniverseManager.Instance.GetStore(ctx).ReloadAsync();
            }
        }

        private string GetRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.Contains("..")) throw new SecurityException("Path traversal detected.");

            string fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, path));
            string normalizedWorkspaceRoot = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar) ? _workspaceRoot : _workspaceRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase) && fullPath != _workspaceRoot)
            {
                 throw new SecurityException("Outside workspace access detected.");
            }

            return Path.GetRelativePath(_workspaceRoot, fullPath);
        }
    }
}
