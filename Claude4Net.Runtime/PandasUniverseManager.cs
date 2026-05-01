using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// TeruTeruPandas의 DataUniverse를 Claude4Net 표준 환경(db/memory.db)에서 
    /// 싱글톤 및 스레드 안전한 트랜잭션 큐 방식으로 관리하는 매니저입니다.
    /// 외부 라이브러리인 TeruTeruPandas를 수정하지 않고 운영 규칙을 강제합니다.
    /// </summary>
    public class PandasUniverseManager
    {
        private static readonly Lazy<PandasUniverseManager> _instance = new Lazy<PandasUniverseManager>(() => new PandasUniverseManager());
        public static PandasUniverseManager Instance => _instance.Value;

        private readonly DataUniverse _universe;
        private readonly string _dbPath;
        private readonly Channel<Func<DataUniverse, Task>> _transactionQueue;
        private bool _isDirty = false;

        public IEnumerable<string> TableNames => _universe.TableNames;

        private PandasUniverseManager()
        {
            // 1. 실행파일 경로 아래 db/memory.db 경로 확정
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbDir = Path.Combine(baseDir, "db");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            _dbPath = Path.Combine(dbDir, "memory.db");

            // 2. 초기 로드 (db/memory.db가 있으면 SqliteIO로 로드)
            if (File.Exists(_dbPath))
            {
                try
                {
                    _universe = DataUniverseIO.FromSqlite(_dbPath);
                }
                catch (Exception)
                {
                    // 로드 실패 시 빈 유니버스로 시작
                    _universe = new DataUniverse();
                }
            }
            else
            {
                _universe = new DataUniverse();
            }

            // 3. 트랜잭션 큐 초기화 (순차 처리를 위해 Unbounded 사용)
            _transactionQueue = Channel.CreateUnbounded<Func<DataUniverse, Task>>();

            // 4. Ensure baseline tables (agent_memory, agent_trajectories) exist
            _ = EnsureBaselineTablesAsync();

            // 5. 백그라운드 큐 처리 루프 시작
            _ = ProcessQueueAsync();

            // 6. 10분 단위 자동 저장 백그라운드 루프 시작
            _ = AutoSaveLoopAsync();

            // 7. 앱 강제 종료 감지 시 남은 데이터 저장 보장
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_isDirty)
                {
                    // 콘솔 종료 시 동기적으로 즉시 강제 덮어쓰기
                    try { _universe.ToSqlite(_dbPath, overwrite: true); } catch { }
                }
            };
        }

        private async Task EnsureBaselineTablesAsync()
        {
            await ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory"))
                {
                    var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                    {
                        ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["Role"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["CurrentTask"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["SharedContext"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["LastUpdated"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["SessionId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["Keywords"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["UserPrompt"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["AgentResponse"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(0)
                    };
                    u.AddTable("agent_memory", new DataFrame(columns), "Shared agent state and long-term memory for RAG.");
                }
                else
                {
                    // Migration: Ensure all columns exist
                    var df = u.GetTableOrThrow("agent_memory");
                    var requiredCols = new Dictionary<string, Func<int, TeruTeruPandas.Core.Column.IColumn>>
                    {
                        ["Keywords"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(Enumerable.Repeat("", n).ToArray()),
                        ["UserPrompt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(Enumerable.Repeat("", n).ToArray()),
                        ["AgentResponse"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(Enumerable.Repeat("", n).ToArray()),
                        ["Embedding"] = (n) => new TeruTeruPandas.Core.Column.VectorColumn(n)
                    };

                    bool modified = false;
                    foreach (var pair in requiredCols)
                    {
                        if (!df.Columns.Contains(pair.Key))
                        {
                            df.AddColumn(pair.Key, pair.Value(df.RowCount));
                            modified = true;
                        }
                    }
                    if (modified) u.AddOrUpdateTable("agent_memory", df);
                }

                if (!u.ContainsTable("agent_trajectories"))
                {
                    var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                    {
                        ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["IsError"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["ErrorReason"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                        ["Payload"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0])
                    };
                    u.AddTable("agent_trajectories", new DataFrame(columns), "Execution history for self-reflection and auditing.");
                }
            });
        }

        private async Task AutoSaveLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
                if (_isDirty)
                {
                    // 큐에 저장 트랜잭션을 삽입하여 동시성 충돌 방지
                    await ExecuteAsync(u =>
                    {
                        Save(u);
                        _isDirty = false;
                    });
                }
            }
        }

        /// <summary>
        /// DataUniverse에 대한 모든 작업(읽기/쓰기/SQL)을 큐에 쌓아 순차적으로 실행합니다.
        /// 실행 후 변경 사항은 자동으로 db/memory.db에 저장됩니다.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            await _transactionQueue.Writer.WriteAsync(async u =>
            {
                try
                {
                    T result = action(u);
                    _isDirty = true; // 변경 사항 발생 마킹 (저장은 10분마다 일괄 처리)
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                await Task.CompletedTask;
            });

            return await tcs.Task;
        }

        /// <summary>
        /// 반환값이 없는 작업을 실행합니다.
        /// </summary>
        public async Task ExecuteAsync(Action<DataUniverse> action)
        {
            await ExecuteAsync<object?>((Func<DataUniverse, object?>)(u =>
            {
                action(u);
                return null;
            }));
        }

        /// <summary>
        /// 비동기 작업을 포함하는 실행 방식입니다.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>();

            await _transactionQueue.Writer.WriteAsync(async u =>
            {
                try
                {
                    var task = action(u);
                    if (task == null)
                    {
                        tcs.SetException(new InvalidOperationException("Action returned a null task."));
                        return;
                    }
                    T result = await task;
                    _isDirty = true;
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }

        /// <summary>
        /// 반환값이 없는 비동기 작업을 실행합니다.
        /// </summary>
        public async Task ExecuteAsync(Func<DataUniverse, Task> action)
        {
            await ExecuteAsync<object?>(async u =>
            {
                var task = action(u);
                if (task != null) await task;
                return null;
            });
        }

        private void Save(DataUniverse u)
        {
            try
            {
                // TeruTeruPandas의 공식 SqliteIO 확장 메서드를 사용하여 저장
                u.ToSqlite(_dbPath, overwrite: true);
            }
            catch (Exception)
            {
                // 로그 기록 등 예외 처리 필요
            }
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var transaction in _transactionQueue.Reader.ReadAllAsync())
            {
                await transaction(_universe);
            }
        }
    }
}
