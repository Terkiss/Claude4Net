# TeruTeruPandas 用户指南

## 概述
TeruTeruPandas 是受 pandas 启发的轻量级 C# DataFrame/Series 库。
提供 Core（DataFrame、Series、Index）、IO（CSV/JSON/SQLite）、Compat（pandas 风格助手）。

## 快速开始
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

var df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["name"] = new object[] { "A", "B", "C" },
    ["score"] = new object[] { 10, 20, 30 }
});

Console.WriteLine(df.Head(2));
```

## 命名空间
- `TeruTeruPandas.Core`：DataFrame、Series、Index、Join/Pivot、GroupBy
- `TeruTeruPandas.Compat`：pandas 风格扩展方法和 `Pd` 助手
- `TeruTeruPandas.IO`：CSV/JSON/SQLite 输入输出

## 核心对象
### DataFrame
- 创建：`new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- 属性：`RowCount`、`ColumnCount`、`Columns`、`Index`、`Values`、`Dtypes`
- 索引器：
  - `df["col"]`：列访问
  - `df[row, "col"]`：行/列访问
  - `df[rowKey, "col"]`：标签访问
  - `df[mask]`：BoolSeries 过滤
- 主要方法：`Head`、`Tail`、`DropNA`、`Info`、`Describe`、`IsNa`、`NotNa`
- 统计：`Std`、`Var`、`Median`、`Min`、`Max`、`Quantile`
- 累积/变化：`Cumsum`、`Cumprod`、`Cummax`、`Cummin`、`Diff`、`PctChange`

### Series<T> / BoolSeries
- `Series<T>`：支持 NA 的一维向量（`IsNA`、`SetNA`）
- `BoolSeries`：布尔掩码，支持 `&`、`|`、`!`

### Index
- `RangeIndex`、`IntIndex`、`StringIndex`、`DateTimeIndex`
- DataFrame/Series 支持基于位置和标签的 Index 访问

## 索引
- 位置：`df.ILoc[row, col]` 或 `df.Iat(row, col)`
- 标签：`df.Loc[rowKey, col]` 或 `df.At(rowKey, col)`
- 掩码：`df[BoolSeries]`

## NA 处理
- 列保留 NA 掩码
- 用 `DropNA()` 删除包含 NA 的行
- 用 `FillNa(value)`（Compat）将 NA 填充为常数

## 分组与聚合
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- 支持：`sum`、`mean`、`count`、`max`、`min`、`std`、`var`

## 连接与合并
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- 重复列名可能带有 `_right` 后缀

## 透视与展开
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## 输入输出（IO）
### CSV
- 读取：`CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- 写入：`CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- 读取：`JsonIO.ReadJson(path, isJsonLines: false)`
- 写入：`JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- 读取：`SqliteIO.ReadSqlite(connectionString, query)`
- 写入：`SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- 表名列表：`SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- 通过 `AddTable`、`UpdateTable`、`GetTable`、`RemoveTable` 管理多个 DataFrame
- 通过 `Join`、`ConcatTables`、`SqlExecute` 查询和操作
- 可整体保存/恢复为 JSON、目录（CSV）、SQLite

## SQL 支持
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- 支持：`SELECT`、`WHERE`、`JOIN`、`GROUP BY`、`ORDER BY`、`LIMIT`
- 注：`ORDER BY` 当前保持原始顺序，不排序

## 限制
- 类型推断基于样本，主要适用于 string/number/bool
- `Query` 仅支持简单表达式（如 "col > 5"）
- `SimplePivot`/`SimpleMelt` 在大数据量下可能较慢
- NA 处理方式可能与 pandas 不同
