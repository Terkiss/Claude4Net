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

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string path = input?["path"] ?? throw new ArgumentException("Path is required");
            string tableName = input?["tableName"] ?? throw new ArgumentException("TableName is required");

            if (!File.Exists(path))
                return new { status = "Error", message = $"File not found: {path}" };

            try
            {
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

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, string>>(arguments);
            string path = input?["path"] ?? throw new ArgumentException("Path is required");
            string tableName = input?["tableName"] ?? throw new ArgumentException("TableName is required");

            if (!File.Exists(path))
                return new { status = "Error", message = $"File not found: {path}" };

            try
            {
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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

        public async Task<object> ExecuteAsync(string arguments, object context)
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
}
