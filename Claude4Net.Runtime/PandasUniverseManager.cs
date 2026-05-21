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



        private readonly DataUniverse _universe;
        private readonly string _dbPath;
        private readonly Channel<Func<DataUniverse, Task>> _transactionQueue;
        private bool _isDirty = false;

        /// <summary>
        /// ?꾩옱 ?좊땲踰꾩뒪???ы븿???뚯씠釉??대쫫 紐⑸줉?낅땲??
        /// </summary>
        public IEnumerable<string> TableNames => _universe.TableNames;

        private PandasUniverseManager()
        {
            // 1. ?곗씠?곕쿋?댁뒪 ???寃쎈줈 ?뺤젙 (db/memory.db)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbDir = Path.Combine(baseDir, "db");
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            _dbPath = Path.Combine(dbDir, "memory.db");

            // 2. 湲곗〈 DB 濡쒕뱶 ?쒕룄
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

            // 3. ?몃옖??뀡 ??珥덇린?? 紐⑤뱺 DB ?묒뾽? ?먮? ?듯빐 ?쒖감?곸쑝濡?泥섎━?섏뼱 ?ㅻ젅???덉쟾?깆쓣 蹂댁옣?⑸땲??
            _transactionQueue = Channel.CreateUnbounded<Func<DataUniverse, Task>>();

            // 4. ?꾩닔 踰좎씠?ㅻ씪???뚯씠釉?Schema) 珥덇린??
            EnsureBaselineTablesInternal(_universe);

            // 5. 諛깃렇?쇱슫???몃옖??뀡 泥섎━ 猷⑦봽 ?쒖옉
            _ = ProcessQueueAsync();

            // 6. 10遺?二쇨린 ?먮룞 ???猷⑦봽 ?쒖옉
            _ = AutoSaveLoopAsync();

            // 7. ?좏뵆由ъ??댁뀡 醫낅즺 ???곗씠??媛뺤젣 ???蹂댁옣
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                if (_isDirty)
                {
                    try { _universe.ToSqlite(_dbPath, overwrite: true); } catch { }
                }
            };
        }

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

        private async Task AutoSaveLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(10));
                if (_isDirty)
                {
                    // ?몃옖??뀡 ?먮? ?듯빐 ?쇨????덇쾶 ????묒뾽 ?섑뻾
                    await ExecuteAsync(u =>
                    {
                        Save(u);
                        _isDirty = false;
                    });
                }
            }
        }

        /// <summary>
        /// DataUniverse?????諛섑솚媛믪씠 ?덈뒗 ?묒뾽???쒖감?곸쑝濡??ㅽ뻾?⑸땲??
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
        /// DataUniverse?????諛섑솚媛믪씠 ?녿뒗 ?묒뾽???쒖감?곸쑝濡??ㅽ뻾?⑸땲??
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
        /// DataUniverse?????諛섑솚媛믪씠 ?덈뒗 鍮꾨룞湲??묒뾽???쒖감?곸쑝濡??ㅽ뻾?⑸땲??
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
        /// DataUniverse?????諛섑솚媛믪씠 ?녿뒗 鍮꾨룞湲??묒뾽???쒖감?곸쑝濡??ㅽ뻾?⑸땲??
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
                // ?몃찓紐⑤━ ?좊땲踰꾩뒪???ㅻ깄?룹쓣 SQLite ?뚯씪濡??곴뎄 ???
                u.ToSqlite(_dbPath, overwrite: true);
            }
            catch (Exception)
            {
                // ????ㅻ쪟 ??蹂꾨룄??濡쒓퉭?대굹 蹂듦뎄 濡쒖쭅 ?꾩슂
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
        /// ?뚯뒪??寃⑸━瑜??꾪빐 ?몃옖??뀡 ?먮? ?쒖감?곸쑝濡??듦낵?섎뒗 ?덉쟾??鍮꾨룞湲?由ъ뀑 ?묒뾽???섑뻾?⑸땲??
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
