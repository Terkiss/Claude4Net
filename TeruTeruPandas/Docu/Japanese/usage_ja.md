# テルテルパンダス ユーザーガイド

## 概要
TeruTeruPandasは、pandasにインスパイアされた軽量C# DataFrame/Seriesライブラリです。
Core（DataFrame, Series, Index）、IO（CSV/JSON/SQLite）、Compat（pandas風ヘルパー）を提供します。

## クイックスタート
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

## 名前空間
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: pandas風拡張メソッドと`Pd`ヘルパー
- `TeruTeruPandas.IO`: CSV/JSON/SQLite入出力

## コアオブジェクト
### DataFrame
- 作成: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- プロパティ: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- インデクサ:
  - `df["col"]`: カラムアクセス
  - `df[row, "col"]`: 行/カラムアクセス
  - `df[rowKey, "col"]`: ラベルベースアクセス
  - `df[mask]`: BoolSeriesフィルタ
- 主なメソッド: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- 統計: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- 累積/変化: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: NA対応1次元ベクトル（`IsNA`, `SetNA`）
- `BoolSeries`: ブールマスク、`&`, `|`, `!`対応

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Seriesは位置・ラベルベースアクセスをIndexでサポート

## インデクシング
- 位置: `df.ILoc[row, col]` または `df.Iat(row, col)`
- ラベル: `df.Loc[rowKey, col]` または `df.At(rowKey, col)`
- マスク: `df[BoolSeries]`

## NA処理
- カラムはNAマスクを保持
- `DropNA()`でNAを含む行を削除
- `FillNa(value)`（Compat）でNAを定数で埋める

## グループ化と集計
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- サポート: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## 結合と連結
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- 重複カラム名には`_right`サフィックスが付く場合があります。

## ピボットとメルト
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## 入出力(IO)
### CSV
- 読み込み: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- 書き込み: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- 読み込み: `JsonIO.ReadJson(path, isJsonLines: false)`
- 書き込み: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- 読み込み: `SqliteIO.ReadSqlite(connectionString, query)`
- 書き込み: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- テーブル一覧: `SqliteIO.GetTableNames(dbPath)`

## データユニバース(DataUniverse)
- 複数のDataFrameを`AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`で管理
- `Join`, `ConcatTables`, `SqlExecute`でクエリや操作
- 全体をJSON、ディレクトリ(CSV)、SQLiteで保存/復元可能

## SQLサポート
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- サポート: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- 注: `ORDER BY`は現在並び替えなしで元の順序を保持

## 制限事項
- 型推論はサンプルベースでstring/number/bool中心
- `Query`は単純な式（例: "col > 5"）のみ対応
- `SimplePivot`/`SimpleMelt`は大規模データで遅い場合あり
- NAの扱いはpandasと一部異なる場合あり
