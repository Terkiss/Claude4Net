using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.SDK.Terukirdo;

namespace Claude4Net.Runtime.Terukirdo
{
    /// <summary>
    /// 이중 평면(Dual-Plane) 메모리 및 궤적 동기화 서비스 구현체
    /// </summary>
    public class TerukirdoMemoryService : ITerukirdoMemoryService
    {
        private readonly IAgentEventBroadcaster? _broadcaster;
        private readonly string _trajectoryPath;
        private readonly string _masterMemoryPath;
        private static readonly SemaphoreSlim _fileLock = new(1, 1);

        public TerukirdoMemoryService(IAgentEventBroadcaster? broadcaster = null, string? workspaceRoot = null)
        {
            _broadcaster = broadcaster;
            string root = workspaceRoot ?? AppState.CurrentCwd ?? Environment.CurrentDirectory;
            string docsDir = Path.Combine(root, "docs");
            if (!Directory.Exists(docsDir))
            {
                Directory.CreateDirectory(docsDir);
            }
            _trajectoryPath = Path.Combine(docsDir, "Terukirdo_Trajectory.txt");
            _masterMemoryPath = Path.Combine(docsDir, "Terukirdo_memory.txt");
        }

        public async Task AppendTrajectoryEventAsync(string eventSummary, string rawEvidence, CancellationToken ct = default)
        {
            await _fileLock.WaitAsync(ct);
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string entry = $"\n[{timestamp}] {eventSummary}\n- Evidence: {rawEvidence}\n";
                await File.AppendAllTextAsync(_trajectoryPath, entry, ct);

                if (_broadcaster != null)
                {
                    await _broadcaster.BroadcastAsync(new TerukirdoMemorySyncedEvent
                    {
                        TrajectoryPath = _trajectoryPath,
                        EntriesSynced = 1
                    });
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveMasterPreferenceAsync(string key, string value, bool userOptInConfirmed, CancellationToken ct = default)
        {
            if (!userOptInConfirmed)
            {
                throw new SecurityException("Prime Directive Violation: Cannot save master preferences without explicit user opt-in confirmation.");
            }

            await _fileLock.WaitAsync(ct);
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string entry = $"[{timestamp}] {key}: {value}\n";
                await File.AppendAllTextAsync(_masterMemoryPath, entry, ct);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SyncAllAsync(CancellationToken ct = default)
        {
            // Sync flush
            await Task.CompletedTask;
        }
    }
}
