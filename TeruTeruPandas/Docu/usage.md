# TeruTeruPandas User Guide

## Overview
TeruTeruPandas is a lightweight C# DataFrame/Series library inspired by pandas.
It provides Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite), and Compat (pandas-like helpers).

## Quick Start
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

## Namespaces
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: pandas-style extension methods and `Pd` helper
- `TeruTeruPandas.IO`: CSV/JSON/SQLite IO

## Core Objects
### DataFrame
- Create: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Properties: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indexers:
  - `df["col"]`: column access
  - `df[row, "col"]`: row/column access
  - `df[rowKey, "col"]`: label-based access
  - `df[mask]`: BoolSeries filtering
- Common methods: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Stats: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Cumulative/change: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: 1D vector with NA handling (`IsNA`, `SetNA`)
- `BoolSeries`: boolean mask with `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series supports position and label-based access via Index.

## Indexing
- Position: `df.ILoc[row, col]` or `df.Iat(row, col)`
- Label: `df.Loc[rowKey, col]` or `df.At(rowKey, col)`
- Mask: `df[BoolSeries]`

## NA Handling
- Columns keep NA masks.
- `DropNA()` removes rows with NA values.
- `FillNa(value)` (Compat) fills NA with a constant.

## Grouping and Aggregation
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Supported: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Join and Concat
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Duplicate column names may get `_right` suffix.

## Pivot and Melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## IO
### CSV
- Read: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Write: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Read: `JsonIO.ReadJson(path, isJsonLines: false)`
- Write: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Read: `SqliteIO.ReadSqlite(connectionString, query)`
- Write: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Table list: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Manage multiple DataFrames with `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Query with `Join`, `ConcatTables`, `SqlExecute`
- Persist to JSON, directory (CSV), or SQLite

## SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Supported: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Note: `ORDER BY` is currently a no-op (keeps original order).

## Limitations
- Type inference is sample-based and favors string/number/bool.
- `Query` only accepts simple expressions like `"col > 5"`.
- `SimplePivot`/`SimpleMelt` are simple implementations and can be slow on large data.
- NA semantics may differ from pandas in edge cases.
