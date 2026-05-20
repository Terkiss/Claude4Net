using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// TeruTeruPandas의 DataUniverse를 관리하는 싱글톤 매니저입니다.
    /// 인메모리 데이터의 스레드 안전한 트랜잭션 처리와 SQLite 기반의 영구 저장을 담당합니다.
    /// </summary>
    public class PandasUniverseManager
    {
        private static readonly Lazy<PandasUniverseManager> _instance = new Lazy<PandasUniverseManager>(() => new PandasUniverseManager());
        
        /// <summary>
        /// PandasUniverseManager의 싱글톤 인스턴스입니다.
        /// </summary>
        public static PandasUniverseManager Instance => _instance.Value;

        private readonly DataUniverse _universe;
        private readonly string _dbPath;
        private readonly Channel<Func<DataUniverse, Task>> _transactionQueue;
        private bool _isDirty = false;

        /// <summary>
        /// 현재 유니버스에 포함된 테이블 이름 목록입니다.
        /// </summary>
        public IEnumerable<string> TableNames => _universe.TableNames;

        private PandasUniverseManager()
        {
            // 1. 데이터베이스 저장 경로 확정 (db/memory.db)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbDir = Path.Combine(baseDir, "db");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            _dbPath = Path.Combine(dbDir, "memory.db");

            // 2. 기존 DB 로드 시도
            if (File.Exists(_dbPath))
            {
                try
                {
                    _universe = DataUniverseIO.FromSqlite(_dbPath);
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

            // 3. 트랜잭션 큐 초기화: 모든 DB 작업은 큐를 통해 순차적으로 처리되어 스레드 안전성을 보장합니다.
            _transactionQueue = Channel.CreateUnbounded<Func<DataUniverse, Task>>();

            // 4. 필수 베이스라인 테이블(Schema) 초기화
            EnsureBaselineTablesInternal(_universe);

            // 5. 백그라운드 트랜잭션 처리 루프 시작
            _ = ProcessQueueAsync();

            // 6. 10분 주기 자동 저장 루프 시작
            _ = AutoSaveLoopAsync();

            // 7. 애플리케이션 종료 시 데이터 강제 저장 보장
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_isDirty)
                {
                    try { _universe.ToSqlite(_dbPath, overwrite: true); } catch { }
                }
            };
        }

        /// <summary>
        /// 필수 베이스라인 테이블이 존재하는지 비동기로 확인하고 생성합니다.
        /// </summary>
        public async Task EnsureBaselineTablesAsync()
        {
            await ExecuteAsync(u =>
            {
                EnsureBaselineTablesInternal(u);
            });
        }

        /// <summary>
        /// DataUniverse 내부에 필요한 핵심 테이블(메모리, 궤적, 감사 로그, 임베딩 캐시) 스키마를 생성합니다.
        /// </summary>
        public void EnsureBaselineTablesInternal(DataUniverse u)
        {
            // RAG 및 장기 기억을 위한 테이블
            if (!u.ContainsTable("agent_memory"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Role"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["CurrentTask"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["SharedContext"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["LastUpdated"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["SessionId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Keywords"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["UserPrompt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AgentResponse"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(0)
                };
                u.AddTable("agent_memory", new DataFrame(columns), "Shared agent state and long-term memory for RAG.");
            }
            else
            {
                // 기존 테이블이 있는 경우 누락된 컬럼에 대한 마이그레이션 수행
                var df = u.GetTableOrThrow("agent_memory");
                var requiredCols = new Dictionary<string, Func<int, TeruTeruPandas.Core.Column.IColumn>>
                {
                    ["Keywords"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["UserPrompt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["AgentResponse"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
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

            // 에이전트의 실행 궤적(Trajectories) 저장을 위한 테이블
            if (!u.ContainsTable("agent_trajectories"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["IsError"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ErrorReason"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Payload"] = new TeruTeruPandas.Core.Column.StringColumn(0)
                };
                u.AddTable("agent_trajectories", new DataFrame(columns), "Execution history for self-reflection and auditing.");
            }

            // 보안 감사 로그 기록을 위한 테이블
            if (!u.ContainsTable("audit_logs"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["User"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Input"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["SafetyResult"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Approved"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(0)
                };
                u.AddTable("audit_logs", new DataFrame(columns), "Security audit trail for sensitive operations.");
            }

            // API 호출 절감을 위한 임베딩 캐시 테이블
            if (!u.ContainsTable("embedding_cache"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Text"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(0),
                    ["LastUsed"] = new TeruTeruPandas.Core.Column.StringColumn(0)
                };
                u.AddTable("embedding_cache", new DataFrame(columns), "Cache for text embeddings to reduce API calls.");
            }
        }

        private async Task AutoSaveLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
                if (_isDirty)
                {
                    // 트랜잭션 큐를 통해 일관성 있게 저장 작업 수행
                    await ExecuteAsync(u =>
                    {
                        Save(u);
                        _isDirty = false;
                    });
                }
            }
        }

        /// <summary>
        /// DataUniverse에 대해 반환값이 있는 작업을 순차적으로 실행합니다.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            await _transactionQueue.Writer.WriteAsync(async u =>
            {
                try
                {
                    T result = action(u);
                    _isDirty = true;
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
        /// DataUniverse에 대해 반환값이 없는 작업을 순차적으로 실행합니다.
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
        /// DataUniverse에 대해 반환값이 있는 비동기 작업을 순차적으로 실행합니다.
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
        /// DataUniverse에 대해 반환값이 없는 비동기 작업을 순차적으로 실행합니다.
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
                // 인메모리 유니버스의 스냅샷을 SQLite 파일로 영구 저장
                u.ToSqlite(_dbPath, overwrite: true);
            }
            catch (Exception)
            {
                // 저장 오류 시 별도의 로깅이나 복구 로직 필요
            }
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var transaction in _transactionQueue.Reader.ReadAllAsync())
            {
                await transaction(_universe);
            }
        }

        /// <summary>
        /// 테스트 격리를 위해 트랜잭션 큐를 순차적으로 통과하는 안전한 비동기 리셋 작업을 수행합니다.
        /// </summary>
        internal async Task ResetAndFlushForTestAsync()
        {
            await ExecuteAsync(u =>
            {
                u.ClearAll();
                EnsureBaselineTablesInternal(u);
                _isDirty = false;
            });

            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch { }
        }
    }
}
