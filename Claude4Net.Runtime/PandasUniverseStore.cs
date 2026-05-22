using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TeruTeruPandas.Core;
using TeruTeruPandas.IO;

namespace Claude4Net.Runtime
{
    public sealed class WorkspaceStateContext
    {
        public string WorkspaceRoot { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;

        public string StateDir => Path.Combine(WorkspaceRoot, ".claude4net", "state");
        public string MemoryDbPath => Path.Combine(StateDir, "memory.db");
        public string SnapshotsDir => Path.Combine(StateDir, "snapshots");

        public string GetSessionCheckpointsStateDir(string checkpointId) =>
            Path.Combine(WorkspaceRoot, ".claude4net", "sessions", SessionId, "checkpoints", checkpointId, "state");
    }

    public interface IPandasUniverseStore
    {
        Task<DataUniverse> LoadAsync(WorkspaceStateContext context, CancellationToken ct = default);
        Task SaveAsync(WorkspaceStateContext context, DataUniverse universe, CancellationToken ct = default);
        Task<string> CreateSnapshotAsync(WorkspaceStateContext context, string reason, CancellationToken ct = default);
        Task RestoreSnapshotAsync(WorkspaceStateContext context, string snapshotId, CancellationToken ct = default);

        // Execute operations safely
        Task<T> ExecuteAsync<T>(Func<DataUniverse, T> action);
        Task ExecuteAsync(Action<DataUniverse> action);
        Task<T> ExecuteAsync<T>(Func<DataUniverse, Task<T>> action);
        Task ExecuteAsync(Func<DataUniverse, Task> action);

        Task ResetAndFlushForTestAsync(); void ForceSaveSync(); Task ReloadAsync();

        IEnumerable<string> TableNames { get; }
    }

    public class ScopedPandasUniverseStore : IPandasUniverseStore
    {
        private readonly WorkspaceStateContext _context;
        private DataUniverse _universe;
        private readonly Channel<Func<DataUniverse, Task>> _transactionQueue;
        private bool _isDirty = false;

        public IEnumerable<string> TableNames => _universe.TableNames;

        public ScopedPandasUniverseStore(WorkspaceStateContext context)
        {
            _context = context;

            if (!Directory.Exists(_context.StateDir))
                Directory.CreateDirectory(_context.StateDir);

            // Migration check: If app base memory.db exists but new one doesn't, we can copy it,
            // but for safety we just log a warning and let it start fresh or manually migrate.

            if (File.Exists(_context.MemoryDbPath))
            {
                try
                {
                    _universe = DataUniverseIO.FromSqlite(_context.MemoryDbPath);
                }
                catch (Exception)
                {
                    _universe = new DataUniverse();
                }
            }
            else
            {
                string legacyDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "db", "memory.db");
                if (File.Exists(legacyDbPath))
                {
                    Console.WriteLine($"[Warning] Active database not found. Loading legacy app-base memory database from '{legacyDbPath}' as read-only fallback.");
                    try
                    {
                        _universe = DataUniverseIO.FromSqlite(legacyDbPath);
                    }
                    catch (Exception)
                    {
                        _universe = new DataUniverse();
                    }
                }
                else
                {
                    _universe = new DataUniverse();
                }
            }

            _transactionQueue = Channel.CreateUnbounded<Func<DataUniverse, Task>>();

            // Baseline tables
            PandasUniverseManager.EnsureBaselineTablesInternal(_universe);

            _ = ProcessQueueAsync();
            _ = AutoSaveLoopAsync();

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_isDirty)
                {
                    try { _universe.ToSqlite(_context.MemoryDbPath, overwrite: true); _isDirty = false; } catch { }
                }
            };
        }

        public Task<DataUniverse> LoadAsync(WorkspaceStateContext context, CancellationToken ct = default)
        {
            return Task.FromResult(_universe);
        }

        public async Task SaveAsync(WorkspaceStateContext context, DataUniverse universe, CancellationToken ct = default)
        {
            await ExecuteAsync(u =>
            {
                u.ToSqlite(context.MemoryDbPath, overwrite: true);
                _isDirty = false;
            });
        }

        public async Task<string> CreateSnapshotAsync(WorkspaceStateContext context, string reason, CancellationToken ct = default)
        {
            string snapshotId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!string.IsNullOrEmpty(reason)) snapshotId += "-" + reason;

            if (!Directory.Exists(context.SnapshotsDir))
                Directory.CreateDirectory(context.SnapshotsDir);

            string path = Path.Combine(context.SnapshotsDir, $"{snapshotId}.db");

            await ExecuteAsync(u =>
            {
                u.ToSqlite(path, overwrite: true);
            });

            return snapshotId;
        }

        public async Task RestoreSnapshotAsync(WorkspaceStateContext context, string snapshotId, CancellationToken ct = default)
        {
            string path = Path.Combine(context.SnapshotsDir, $"{snapshotId}.db");
            if (!File.Exists(path)) throw new FileNotFoundException($"Snapshot {snapshotId} not found");

            await ExecuteAsync(u =>
            {
                var restored = DataUniverseIO.FromSqlite(path);
                u.ClearAll();
                foreach (var tableName in restored.TableNames)
                {
                    u.AddTable(tableName, restored.GetTableOrThrow(tableName));
                }
                PandasUniverseManager.EnsureBaselineTablesInternal(u);
                _isDirty = true;
            });
        }

        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, T> action)
        {
            var tcs = new TaskCompletionSource<T>();
            await _transactionQueue.Writer.WriteAsync(async u =>
            {
                try { T result = action(u); _isDirty = true; tcs.SetResult(result); }
                catch (Exception ex) { tcs.SetException(ex); }
                await Task.CompletedTask;
            });
            return await tcs.Task;
        }

        public async Task ExecuteAsync(Action<DataUniverse> action)
        {
            await ExecuteAsync<object?>((Func<DataUniverse, object?>)(u => { action(u); return null; }));
        }

        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>();
            await _transactionQueue.Writer.WriteAsync(async u =>
            {
                try
                {
                    var task = action(u);
                    if (task == null) { throw new InvalidOperationException("Action returned a null task."); }
                    T result = await task;
                    _isDirty = true;
                    tcs.SetResult(result);
                }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return await tcs.Task;
        }

        public async Task ExecuteAsync(Func<DataUniverse, Task> action)
        {
            await ExecuteAsync<object?>(async u =>
            {
                var task = action(u);
                if (task != null) await task;
                return null;
            });
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var transaction in _transactionQueue.Reader.ReadAllAsync())
            {
                await transaction(_universe);
            }
        }

        private async Task AutoSaveLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
                if (_isDirty)
                {
                    await ExecuteAsync(u =>
                    {
                        try { u.ToSqlite(_context.MemoryDbPath, overwrite: true); _isDirty = false; } catch { }
                    });
                }
            }
        }

        public async Task ResetAndFlushForTestAsync()
        {
            await ExecuteAsync(u =>
            {
                u.ClearAll();
                PandasUniverseManager.EnsureBaselineTablesInternal(u);
                _isDirty = false;
            });

            try
            {
                if (File.Exists(_context.MemoryDbPath)) File.Delete(_context.MemoryDbPath);
            }
            catch { }
        }

        public async Task ReloadAsync() { await ExecuteAsync(u => { if (File.Exists(_context.MemoryDbPath)) { var restored = DataUniverseIO.FromSqlite(_context.MemoryDbPath); u.ClearAll(); foreach (var tableName in restored.TableNames) { u.AddTable(tableName, restored.GetTableOrThrow(tableName)); } PandasUniverseManager.EnsureBaselineTablesInternal(u); } else { u.ClearAll(); PandasUniverseManager.EnsureBaselineTablesInternal(u); } _isDirty = false; }); } public void ForceSaveSync()
        {
            if (_isDirty)
            {
                try { _universe.ToSqlite(_context.MemoryDbPath, overwrite: true); _isDirty = false; } catch { }
            }
        }
    }
}
