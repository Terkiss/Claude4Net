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

            // 4. 백그라운드 큐 처리 루프 시작
            _ = ProcessQueueAsync();
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
                    // 데이터 변경 가능성이 있으므로 매 작업 후 저장 (SqliteIO 사용)
                    Save(u);
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
            await ExecuteAsync<object?>(u =>
            {
                action(u);
                return null;
            });
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
                    T result = await action(u);
                    Save(u);
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
                await action(u);
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
