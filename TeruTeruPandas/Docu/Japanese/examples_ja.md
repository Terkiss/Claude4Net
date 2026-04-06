# TeruTeruPandas 利用例

## 基本的なDataFrame例
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// シンプルなDataFrameを作成
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["名前"] = new object[] { "A", "B", "C" },
    ["スコア"] = new object[] { 10, 20, 30 }
});

// 最初の2行を表示
Console.WriteLine(df.Head(2));
```

## CSVの読み書き
```csharp
using TeruTeruPandas.IO;

// CSVファイルを読み込む
dynamic df = CsvReader.ReadCsv("data.csv", hasHeader: true);

// DataFrameをCSVとして保存
CsvWriter.ToCsv(df, "output.csv");
```

## DataUniverseでSQLクエリ
```csharp
using TeruTeruPandas.Core;

// DataUniverseを作成しテーブルを追加
dynamic universe = new DataUniverse();
universe.AddTable("学生", df);

// SQLクエリを実行
var 結果 = universe.SqlExecute("SELECT 名前, スコア FROM 学生 WHERE スコア > 15");
```
