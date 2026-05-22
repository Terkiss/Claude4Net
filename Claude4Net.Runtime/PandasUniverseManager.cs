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
    /// TeruTeruPandas??DataUniverse瑜?愿由ы븯???깃???留ㅻ땲??낅땲??
    /// ?몃찓紐⑤━ ?곗씠?곗쓽 ?ㅻ젅???덉쟾???몃옖??뀡 泥섎━? SQLite 湲곕컲???곴뎄 ??μ쓣 ?대떦?⑸땲??
    /// </summary>
    public class PandasUniverseManager
    {
        private static readonly Lazy<PandasUniverseManager> _instance = new Lazy<PandasUniverseManager>(() => new PandasUniverseManager());

        /// <summary>
        /// PandasUniverseManager???깃????몄뒪?댁뒪?낅땲??
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
        /// ?꾩닔 踰좎씠?ㅻ씪???뚯씠釉붿씠 議댁옱?섎뒗吏 鍮꾨룞湲곕줈 ?뺤씤?섍퀬 ?앹꽦?⑸땲??
        /// </summary>
        public async Task EnsureBaselineTablesAsync()
        {
            await ExecuteAsync(u =>
            {
                EnsureBaselineTablesInternal(u);
            });
        }

        /// <summary>
        /// DataUniverse ?대????꾩슂???듭떖 ?뚯씠釉?硫붾え由? 沅ㅼ쟻, 媛먯궗 濡쒓렇, ?꾨쿋??罹먯떆) ?ㅽ궎留덈? ?앹꽦?⑸땲??
        /// </summary>
        public static void EnsureBaselineTablesInternal(DataUniverse u)
        {
            // RAG 諛??κ린 湲곗뼲???꾪븳 ?뚯씠釉?
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
                // 湲곗〈 ?뚯씠釉붿씠 ?덈뒗 寃쎌슦 ?꾨씫??而щ읆?????留덉씠洹몃젅?댁뀡 ?섑뻾
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

            // ?먯씠?꾪듃???ㅽ뻾 沅ㅼ쟻(Trajectories) ??μ쓣 ?꾪븳 ?뚯씠釉?
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

            // 蹂댁븞 媛먯궗 濡쒓렇 湲곕줉???꾪븳 ?뚯씠釉?
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

            // API ?몄텧 ?덇컧???꾪븳 ?꾨쿋??罹먯떆 ?뚯씠釉?
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
