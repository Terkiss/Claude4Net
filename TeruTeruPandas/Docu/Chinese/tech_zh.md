# TeruTeruPandas 技术文档

## 架构
TeruTeruPandas 采用 C# 实现，主要包含以下模块：
- **Core**：DataFrame、Series、Index、GroupBy、Join、Pivot、Melt
- **IO**：CSV、JSON、SQLite（读写）
- **Compat**：pandas 风格的辅助方法
- **DataUniverse**：多 DataFrame 管理、SQL 接口、大规模 IO

## 主要类与接口
- `DataFrame`：主要表格对象，支持索引、过滤、聚合、合并
- `Series<T>`：支持 NA 的一维向量
- `Index`：RangeIndex、IntIndex、StringIndex、DateTimeIndex 的基类
- `BoolSeries`：用于过滤的布尔掩码
- `DataUniverse`：多 DataFrame 容器，支持 SQL 查询和批量 IO

## SQL 解析与执行
- 支持基本 SQL 查询：SELECT、WHERE、JOIN、GROUP BY、ORDER BY、LIMIT
- 解析器在 DataUniverseSql.cs，执行在 SqlQueryExecutor
- 限制：仅支持简单表达式，不支持子查询

## IO 模块
- **CsvReader/CsvWriter**：CSV 文件读写
- **JsonIO**：JSON（普通/JSON Lines）读写
- **SqliteIO**：SQLite 表读取与查询，DataFrame 导出为表

## API 使用示例
```csharp
// 读取 CSV
var df = CsvReader.ReadCsv("data.csv");

// 分组与聚合
var grouped = df.GroupBy("类别").Agg(new Dictionary<string, string[]> { ["数值"] = new[] { "sum", "mean" } });

// SQL 查询
var universe = new DataUniverse();
universe.AddTable("表", df);
var result = universe.SqlExecute("SELECT * FROM 表 WHERE 数值 > 10");
```

## 限制与性能
- 针对小型和中型数据集（约 100 万行以内）优化
- 不支持多线程和分布式计算
- 某些操作（Pivot、Melt、复杂 Join）在大数据量下可能较慢

## 扩展与贡献
- 代码可扩展：可添加新的 IO 模块、索引类型、聚合方法
- 与外部数据库集成可用 SqliteIO 或自定义 IO 模块实现
