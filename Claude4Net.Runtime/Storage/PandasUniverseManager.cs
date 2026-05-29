using System;
using System.IO;
using System.Collections.Generic; using System.Collections.Concurrent;
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

        private readonly ConcurrentDictionary<string, IPandasUniverseStore> _storeCache = new();

        public IPandasUniverseStore GetStore(WorkspaceStateContext ctx)
        {
            string key = $"{ctx.WorkspaceRoot}|{ctx.SessionId}";
            return _storeCache.GetOrAdd(key, _ => new ScopedPandasUniverseStore(ctx));
        }

        public static WorkspaceStateContext GetCurrentContext()
        {
            string? root = Claude4Net.SDK.AppState.CurrentCwd;
            if (string.IsNullOrEmpty(root))
            {
                root = Directory.GetCurrentDirectory();
            }
            return new WorkspaceStateContext
            {
                WorkspaceRoot = root,
                SessionId = Claude4Net.SDK.AppState.SessionId ?? "default-session"
            };
        }

        /// <summary>
        /// Gets the table names from the active scoped store.
        /// </summary>
        public IEnumerable<string> TableNames => GetStore(GetCurrentContext()).TableNames;

        /// <summary>
        /// 필수 베이스라인 테이블이 존재치 않는 경우 비동기로 확인하고 생성합니다.
        /// </summary>
        public async Task EnsureBaselineTablesAsync()
        {
            await ExecuteAsync(u =>
            {
                EnsureBaselineTablesInternal(u);
            });
        }

        /// <summary>
        /// DataUniverse 초기 생성 시 필요한 핵심 테이블(메모리, 궤적, 감사 로그, 임베딩 캐시, 인증 관련 테이블 등) 스키마를 생성합니다.
        /// </summary>
        public static void EnsureBaselineTablesInternal(DataUniverse u)
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

            // 에이전트 실행 궤적(Trajectories) 저장을 위한 테이블
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

            // android_pairing_requests 테이블
            if (!u.ContainsTable("android_pairing_requests"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["PairingId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["DeviceName"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AppInstanceId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["CodeHash"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["CreatedAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ExpiresAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AttemptCount"] = new TeruTeruPandas.Core.Column.PrimitiveColumn<int>(0),
                    ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(0)
                };
                u.AddTable("android_pairing_requests", new DataFrame(columns), "Android device pairing requests.");
            }
            else
            {
                var df = u.GetTableOrThrow("android_pairing_requests");
                var requiredCols = new Dictionary<string, Func<int, TeruTeruPandas.Core.Column.IColumn>>
                {
                    ["PairingId"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["DeviceName"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["AppInstanceId"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["CodeHash"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["CreatedAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["ExpiresAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["AttemptCount"] = (n) => new TeruTeruPandas.Core.Column.PrimitiveColumn<int>(n),
                    ["Status"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n)
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
                if (modified) u.AddOrUpdateTable("android_pairing_requests", df);
            }

            // android_auth_tokens 테이블
            if (!u.ContainsTable("android_auth_tokens"))
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["TokenId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["DeviceName"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AppInstanceId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["TokenHash"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["Scopes"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["AuthMethod"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ClientIp"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["CreatedAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["ExpiresAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["LastUsedAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["LastExtendedAt"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                    ["RefreshEligibleAt"] = new TeruTeruPandas.Core.Column.StringColumn(0)
                };
                u.AddTable("android_auth_tokens", new DataFrame(columns), "Android auth tokens.");
            }
            else
            {
                var df = u.GetTableOrThrow("android_auth_tokens");
                var requiredCols = new Dictionary<string, Func<int, TeruTeruPandas.Core.Column.IColumn>>
                {
                    ["TokenId"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["DeviceName"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["AppInstanceId"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["TokenHash"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["Scopes"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["AuthMethod"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["ClientIp"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["CreatedAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["ExpiresAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["LastUsedAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["LastExtendedAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n),
                    ["RefreshEligibleAt"] = (n) => new TeruTeruPandas.Core.Column.StringColumn(n)
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
                if (modified) u.AddOrUpdateTable("android_auth_tokens", df);
            }
        }

        /// <summary>
        /// Execute operation on the active scoped store.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, T> action)
        {
            return await GetStore(GetCurrentContext()).ExecuteAsync(action);
        }

        /// <summary>
        /// Execute operation on the active scoped store.
        /// </summary>
        public async Task ExecuteAsync(Action<DataUniverse> action)
        {
            await GetStore(GetCurrentContext()).ExecuteAsync(action);
        }

        /// <summary>
        /// Execute operation on the active scoped store.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<DataUniverse, Task<T>> action)
        {
            return await GetStore(GetCurrentContext()).ExecuteAsync(action);
        }

        /// <summary>
        /// Execute operation on the active scoped store.
        /// </summary>
        public async Task ExecuteAsync(Func<DataUniverse, Task> action)
        {
            await GetStore(GetCurrentContext()).ExecuteAsync(action);
        }

        /// <summary>
        /// Reset and flush the active scoped store.
        /// </summary>
        internal async Task ResetAndFlushForTestAsync()
        {
            await GetStore(GetCurrentContext()).ResetAndFlushForTestAsync();
        }
    }
}
