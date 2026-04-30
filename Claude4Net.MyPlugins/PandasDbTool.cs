using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using TeruTeruPandas.Core;
using TeruTeruPandas.IO;

namespace Claude4Net.Tools
{
    public class PandasLoadCsvTool : ITool
    {
        public string Name => "pandas_load_csv";
        public string Description => "Load a CSV file into a TeruTeruPandas table within the DataUniverse.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "The path to the CSV file." },
                tableName = new { type = "string", description = "The name of the table to create in the DataUniverse." }
            },
            required = new[] { "path", "tableName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string path = input?["path"] ?? throw new ArgumentException("Path is required");
            string tableName = input?["tableName"] ?? throw new ArgumentException("TableName is required");

            if (!File.Exists(path))
                return new { status = "Error", message = $"File not found: {path}" };

            try
            {
                string fileContent = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(fileContent))
                {
                    var emptyDf = new TeruTeruPandas.Core.DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>());
                    await PandasUniverseManager.Instance.ExecuteAsync(u => u.AddOrUpdateTable(tableName, emptyDf));
                    return new { status = "Success", message = $"Loaded 0 rows into table '{tableName}' (Empty CSV initialized).", columns = emptyDf.Columns };
                }

                var df = CsvReader.ReadCsv(path);
                await PandasUniverseManager.Instance.ExecuteAsync(u => u.AddOrUpdateTable(tableName, df));
                
                return new
                {
                    status = "Success",
                    message = $"Loaded {df.RowCount} rows into table '{tableName}'.",
                    columns = df.Columns
                };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasLoadJsonTool : ITool
    {
        public string Name => "pandas_load_json";
        public string Description => "Load a JSON file (array of records) into a TeruTeruPandas table within the DataUniverse.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "The path to the JSON file." },
                tableName = new { type = "string", description = "The name of the table to create in the DataUniverse." }
            },
            required = new[] { "path", "tableName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string path = input?["path"] ?? throw new ArgumentException("Path is required");
            string tableName = input?["tableName"] ?? throw new ArgumentException("TableName is required");

            if (!File.Exists(path))
                return new { status = "Error", message = $"File not found: {path}" };

            try
            {
                string fileContent = File.ReadAllText(path).Trim();
                if (string.IsNullOrEmpty(fileContent) || fileContent == "[]" || fileContent == "{}")
                {
                    var emptyDf = new TeruTeruPandas.Core.DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>());
                    await PandasUniverseManager.Instance.ExecuteAsync(u => u.AddOrUpdateTable(tableName, emptyDf));
                    return new { status = "Success", message = $"Loaded 0 rows into table '{tableName}' (Empty JSON initialized).", columns = emptyDf.Columns };
                }

                var df = JsonIO.ReadJson(path);
                await PandasUniverseManager.Instance.ExecuteAsync(u => u.AddOrUpdateTable(tableName, df));
                
                return new
                {
                    status = "Success",
                    message = $"Loaded {df.RowCount} rows into table '{tableName}'.",
                    columns = df.Columns
                };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasLoadSqliteTool : ITool
    {
        public string Name => "pandas_load_sqlite";
        public string Description => "Load a table from a SQLite database into the DataUniverse.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                dbPath = new { type = "string", description = "The path to the SQLite database file." },
                sqliteTableName = new { type = "string", description = "The name of the table in the SQLite database." },
                targetTableName = new { type = "string", description = "The name of the table to create in the DataUniverse." }
            },
            required = new[] { "dbPath", "sqliteTableName", "targetTableName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string dbPath = input?["dbPath"] ?? throw new ArgumentException("dbPath is required");
            string sqliteTableName = input?["sqliteTableName"] ?? throw new ArgumentException("sqliteTableName is required");
            string targetTableName = input?["targetTableName"] ?? throw new ArgumentException("targetTableName is required");

            if (!File.Exists(dbPath))
                return new { status = "Error", message = $"Database file not found: {dbPath}" };

            try
            {
                var df = SqliteIO.ReadSqliteTable(dbPath, sqliteTableName);
                await PandasUniverseManager.Instance.ExecuteAsync(u => u.AddOrUpdateTable(targetTableName, df));
                
                return new
                {
                    status = "Success",
                    message = $"Loaded {df.RowCount} rows from SQLite table '{sqliteTableName}' into universe table '{targetTableName}'.",
                    columns = df.Columns
                };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasSqlTool : ITool
    {
        public string Name => "pandas_sql";
        public string Description => "Execute a SQL query on the tables currently loaded in the DataUniverse.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                sql = new { type = "string", description = "The SQL query to execute. Supports SELECT, FROM, JOIN, WHERE, GROUP BY, ORDER BY, LIMIT." }
            },
            required = new[] { "sql" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string sql = input?["sql"] ?? throw new ArgumentException("SQL query is required");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    var df = u.SqlExecute(sql);
                    return new
                    {
                        status = "Success",
                        rowCount = df.RowCount,
                        data = df.ToString()
                    };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasShowTablesTool : ITool
    {
        public string Name => "pandas_show_tables";
        public string Description => "List all tables currently loaded in the DataUniverse and their statistics.";

        public object? InputSchema => null;

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var details = await PandasUniverseManager.Instance.ExecuteAsync(u => u.ToString());
                return new
                {
                    status = "Success",
                    details = details
                };
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasTableInfoTool : ITool
    {
        public string Name => "pandas_table_info";
        public string Description => "Get detailed structural information about a specific table in DataUniverse, including column data types and non-null counts.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string", description = "The name of the table to inspect." }
            },
            required = new[] { "tableName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string tableName = input?["tableName"] ?? throw new ArgumentException("tableName is required");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    var df = u.GetTableOrThrow(tableName);
                    var sb = new System.Text.StringBuilder();
                    df.Info(sb);
                    return new
                    {
                        status = "Success",
                        info = sb.ToString()
                    };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasSaveCsvTool : ITool
    {
        public string Name => "pandas_save_csv";
        public string Description => "Save a table from the DataUniverse to a CSV file.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string", description = "The name of the table to save." },
                savePath = new { type = "string", description = "The file path to save the CSV to." }
            },
            required = new[] { "tableName", "savePath" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string tableName = input?["tableName"] ?? throw new ArgumentException("tableName is required");
            string savePath = input?["savePath"] ?? throw new ArgumentException("savePath is required");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    var df = u.GetTableOrThrow(tableName);
                    CsvWriter.ToCsv(df, savePath);
                    return new
                    {
                        status = "Success",
                        message = $"Saved table '{tableName}' to '{savePath}' ({df.RowCount} rows)."
                    };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasSaveJsonTool : ITool
    {
        public string Name => "pandas_save_json";
        public string Description => "Save a table from the DataUniverse to a JSON file.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string", description = "The name of the table to save." },
                savePath = new { type = "string", description = "The file path to save the JSON to." }
            },
            required = new[] { "tableName", "savePath" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string tableName = input?["tableName"] ?? throw new ArgumentException("tableName is required");
            string savePath = input?["savePath"] ?? throw new ArgumentException("savePath is required");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    var df = u.GetTableOrThrow(tableName);
                    JsonIO.ToJson(df, savePath);
                    return new
                    {
                        status = "Success",
                        message = $"Saved table '{tableName}' to '{savePath}' ({df.RowCount} rows)."
                    };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasSaveSqliteTool : ITool
    {
        public string Name => "pandas_save_sqlite";
        public string Description => "Save a table from the DataUniverse to a SQLite database.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string", description = "The name of the table to save." },
                dbPath = new { type = "string", description = "The SQLite database file path." },
                sqliteTableName = new { type = "string", description = "The name of the destination table in the SQLite database." }
            },
            required = new[] { "tableName", "dbPath", "sqliteTableName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string tableName = input?["tableName"] ?? throw new ArgumentException("tableName is required");
            string dbPath = input?["dbPath"] ?? throw new ArgumentException("dbPath is required");
            string sqliteTableName = input?["sqliteTableName"] ?? throw new ArgumentException("sqliteTableName is required");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    var df = u.GetTableOrThrow(tableName);
                    string connectionString = $"Data Source={dbPath}";
                    SqliteIO.ToSqlite(df, connectionString, sqliteTableName, ifExists: true);
                    
                    return new
                    {
                        status = "Success",
                        message = $"Saved table '{tableName}' into SQLite database '{dbPath}' as '{sqliteTableName}' ({df.RowCount} rows)."
                    };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", message = ex.Message };
            }
        }
    }

    public class PandasInsertRowTool : ITool
    {
        public string Name => "pandas_insert_row";
        public string Description => "데이터프레임(테이블)에 새 행(단일 JSON 오브젝트)을 1개 추가합니다. 스키마 누락시 Null로 방어됩니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string" },
                rowJson = new { type = "string", description = "추가할 행의 단일 JSON 오브젝트 문자열 (예: {\"id\": 1, \"name\": \"Test\"})" }
            },
            required = new[] { "tableName", "rowJson" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string tableName = input?["tableName"] ?? throw new ArgumentException("tableName");
            string rowJson = input?["rowJson"] ?? throw new ArgumentException("rowJson");

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.GetTableOrThrow(tableName);
                    string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                    File.WriteAllText(tmpFile, "[" + rowJson + "]");

                    try
                    {
                        var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable(tableName, updatedDf);
                        return new { status = "Success", message = $"1 row inserted.", newRowCount = updatedDf.RowCount };
                    }
                    finally
                    {
                        if (File.Exists(tmpFile)) File.Delete(tmpFile);
                    }
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasUpdateCellTool : ITool
    {
        public string Name => "pandas_update_cell";
        public string Description => "특정 행(정수 인덱스) 및 열의 단일 셀 값을 새로운 값으로 업데이트합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string" },
                rowIndex = new { type = "integer", description = "수정할 대상의 0부터 시작하는 행 정수 인덱스" },
                columnName = new { type = "string" },
                value = new { type = "string", description = "수정할 새 값 (문자열). 컬럼의 본래 데이터 타입에 맞춰내부적으로 파싱됩니다." }
            },
            required = new[] { "tableName", "rowIndex", "columnName", "value" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse(arguments);
            var root = doc.RootElement;
            string tableName = root.GetProperty("tableName").GetString()!;
            int rowIndex = root.GetProperty("rowIndex").GetInt32();
            string columnName = root.GetProperty("columnName").GetString()!;
            string? value = root.TryGetProperty("value", out var v) ? v.GetString() : null;

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.GetTableOrThrow(tableName);
                    if (!df.Dtypes.ContainsKey(columnName)) throw new KeyNotFoundException($"Column {columnName} not found.");
                    
                    var colType = df.Dtypes[columnName];
                    object? castedValue = value == null ? null : Convert.ChangeType(value, colType);

                    df[rowIndex, columnName] = castedValue;
                    return new { status = "Success", message = $"Cell ({rowIndex},{columnName}) updated to {value}." };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasDeleteRowsTool : ITool
    {
        public string Name => "pandas_delete_rows";
        public string Description => "지정된 정수 인덱스들의 행을 테이블에서 제거합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                tableName = new { type = "string" },
                rowIndices = new
                {
                    type = "array",
                    items = new { type = "integer" },
                    description = "삭제할 0부터 시작하는 행 인덱스들의 배열"
                }
            },
            required = new[] { "tableName", "rowIndices" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse(arguments);
            var root = doc.RootElement;
            string tableName = root.GetProperty("tableName").GetString()!;
            var rowIndices = root.GetProperty("rowIndices").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.GetTableOrThrow(tableName);
                    var indicesToKeep = Enumerable.Range(0, df.RowCount).Where(i => !rowIndices.Contains(i)).ToArray();

                    var newColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>();
                    foreach (var colName in df.Columns)
                    {
                        newColumns[colName] = df[colName].Reorder(indicesToKeep);
                    }
                    var newIndex = df.Index.Reorder(indicesToKeep);
                    var newDf = new TeruTeruPandas.Core.DataFrame(newColumns, newIndex);
                    
                    u.AddOrUpdateTable(tableName, newDf);
                    return new { status = "Success", message = $"Deleted {rowIndices.Count} rows. New count: {newDf.RowCount}." };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasSnapshotTool : ITool
    {
        public string Name => "pandas_snapshot";
        public string Description => "지정된 이름으로 현재 DataUniverse의 전체 스냅샷(SQLite 파일)을 생성합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                snapshotName = new { type = "string", description = "스냅샷 파일의 이름 (예: checkpoint_1). 특수문자나 경로는 무시되고 파일명만 사용됩니다." }
            },
            required = new[] { "snapshotName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string rawName = input?["snapshotName"] ?? throw new ArgumentException("snapshotName is required");
            string safeName = Path.GetFileName(rawName); // Basic protection against traversal

            try
            {
                string snapshotDir = Path.Combine(AppState.SystemBaseDir, "db", "snapshots");
                if (!Directory.Exists(snapshotDir)) Directory.CreateDirectory(snapshotDir);
                string snapshotPath = Path.Combine(snapshotDir, $"{safeName}.db");

                await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    u.ToSqlite(snapshotPath, overwrite: true);
                });

                return new { status = "Success", message = $"Snapshot saved to {snapshotPath}" };
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasRestoreTool : ITool
    {
        public string Name => "pandas_restore";
        public string Description => "이전에 저장된 스냅샷(SQLite 파일)으로부터 DataUniverse를 복구합니다. 현재 데이터는 덮어씌워지므로 주의하세요.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                snapshotName = new { type = "string", description = "복구할 스냅샷 파일의 이름" }
            },
            required = new[] { "snapshotName" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string rawName = input?["snapshotName"] ?? throw new ArgumentException("snapshotName is required");
            string safeName = Path.GetFileName(rawName);

            try
            {
                string snapshotPath = Path.Combine(AppState.SystemBaseDir, "db", "snapshots", $"{safeName}.db");
                if (!File.Exists(snapshotPath))
                    return new { status = "Error", message = $"Snapshot file not found: {snapshotPath}" };

                await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var restoredUniverse = DataUniverseIO.FromSqlite(snapshotPath);
                    u.ClearAll();
                    
                    foreach (var tableName in restoredUniverse.TableNames)
                    {
                        u.AddTable(tableName, restoredUniverse.GetTableOrThrow(tableName));
                    }
                });

                return new { status = "Success", message = $"DataUniverse restored from snapshot {safeName}." };
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasAgentMemoryUpsertTool : ITool
    {
        public string Name => "pandas_agent_memory_upsert";
        public string Description => "에이전트 공유 메모리(agent_memory)에 현재 상태를 업데이트하거나 추가합니다. AgentId를 기준으로 Upsert를 수행합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                agentId = new { type = "string" },
                role = new { type = "string" },
                status = new { type = "string" },
                currentTask = new { type = "string" },
                sharedContext = new { type = "string" }
            },
            required = new[] { "agentId" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string agentId = input?["agentId"] ?? throw new ArgumentException("agentId is required");
            string role = input.ContainsKey("role") ? input["role"] : "";
            string status = input.ContainsKey("status") ? input["status"] : "active";
            string currentTask = input.ContainsKey("currentTask") ? input["currentTask"] : "";
            string sharedContext = input.ContainsKey("sharedContext") ? input["sharedContext"] : "";
            string sessionId = AppState.SessionId;
            string timestamp = DateTime.Now.ToString("O");

            try
            {
                await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.GetTableOrThrow("agent_memory");
                    
                    // Find existing row by AgentId
                    int existingIdx = -1;
                    var agentIdCol = df["AgentId"];
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        if (agentIdCol.GetValue(i)?.ToString() == agentId)
                        {
                            existingIdx = i;
                            break;
                        }
                    }

                    if (existingIdx >= 0)
                    {
                        df[existingIdx, "Role"] = role;
                        df[existingIdx, "Status"] = status;
                        df[existingIdx, "CurrentTask"] = currentTask;
                        df[existingIdx, "SharedContext"] = sharedContext;
                        df[existingIdx, "LastUpdated"] = timestamp;
                        df[existingIdx, "SessionId"] = sessionId;
                    }
                    else
                    {
                        // Insert new row
                        var rowDict = new Dictionary<string, object?>
                        {
                            ["AgentId"] = agentId,
                            ["Role"] = role,
                            ["Status"] = status,
                            ["CurrentTask"] = currentTask,
                            ["SharedContext"] = sharedContext,
                            ["LastUpdated"] = timestamp,
                            ["SessionId"] = sessionId
                        };
                        
                        string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                        File.WriteAllText(tmpFile, "[" + JsonSerializer.Serialize(rowDict) + "]");
                        try
                        {
                            var newRowDf = JsonIO.ReadJson(tmpFile);
                            var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                            u.AddOrUpdateTable("agent_memory", updatedDf);
                        }
                        finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                    }
                });
                return new { status = "Success", message = $"Agent memory updated for '{agentId}'." };
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasAgentMemoryQueryTool : ITool
    {
        public string Name => "pandas_agent_memory_query";
        public string Description => "에이전트 공유 메모리에서 특정 조건(SQL)으로 데이터를 조회합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                sql = new { type = "string", description = "조회할 SQL 문 (기본값: SELECT * FROM agent_memory)" }
            }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string sql = (input != null && input.ContainsKey("sql")) ? input["sql"] : "SELECT * FROM agent_memory";

            try
            {
                var result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.SqlExecute(sql);
                    return new { status = "Success", rowCount = df.RowCount, data = df.ToString() };
                });
                return result;
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }

    public class PandasAgentMemoryClearTool : ITool
    {
        public string Name => "pandas_agent_memory_clear";
        public string Description => "에이전트 공유 메모리를 비웁니다. session 파라미터를 제공하면 현재 세션의 데이터만 삭제합니다.";

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                scope = new { type = "string", @enum = new[] { "session", "all" }, description = "삭제 범위 (session: 현재 세션만, all: 전체 삭제)" }
            }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string scope = (input != null && input.ContainsKey("scope")) ? input["scope"] : "session";

            try
            {
                await PandasUniverseManager.Instance.ExecuteAsync(async u =>
                {
                    if (scope == "all")
                    {
                        u.ClearAll();
                        // Re-create baseline tables immediately
                        var memoryColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                        {
                            ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["Role"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["CurrentTask"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["SharedContext"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["LastUpdated"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["SessionId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0])
                        };
                        u.AddTable("agent_memory", new DataFrame(memoryColumns), "Shared agent state synchronization table.");

                        var trajectoryColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                        {
                            ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["IsError"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["ErrorReason"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0]),
                            ["Payload"] = new TeruTeruPandas.Core.Column.StringColumn(new string[0])
                        };
                        u.AddTable("agent_trajectories", new DataFrame(trajectoryColumns), "Execution history for self-reflection and auditing.");
                    }
                    else
                    {
                        var df = u.GetTableOrThrow("agent_memory");
                        var sessionId = AppState.SessionId;
                        var sessionIdCol = df["SessionId"];
                        var indicesToKeep = new List<int>();
                        for (int i = 0; i < df.RowCount; i++)
                        {
                            if (sessionIdCol.GetValue(i)?.ToString() != sessionId)
                            {
                                indicesToKeep.Add(i);
                            }
                        }

                        var newColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>();
                        foreach (var colName in df.Columns)
                        {
                            newColumns[colName] = df[colName].Reorder(indicesToKeep.ToArray());
                        }
                        var newDf = new DataFrame(newColumns, df.Index.Reorder(indicesToKeep.ToArray()));
                        u.AddOrUpdateTable("agent_memory", newDf);
                    }
                });
                return new { status = "Success", message = $"Agent memory cleared (Scope: {scope})." };
            }
            catch (Exception ex)
            {
                return new { status = "Error", error = ex.Message };
            }
        }
    }
}
