# TeruTeruPandas 使用示例

## 基本 DataFrame 示例
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// 创建一个简单的 DataFrame
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["姓名"] = new object[] { "A", "B", "C" },
    ["分数"] = new object[] { 10, 20, 30 }
});

// 显示前两行
Console.WriteLine(df.Head(2));
```

## 读取和写入 CSV
```csharp
using TeruTeruPandas.IO;

// 读取 CSV 文件
dynamic df = CsvReader.ReadCsv("data.csv", hasHeader: true);

// 保存 DataFrame 为 CSV
CsvWriter.ToCsv(df, "output.csv");
```

## 在 DataUniverse 中使用 SQL 查询
```csharp
using TeruTeruPandas.Core;

// 创建 DataUniverse 并添加表
dynamic universe = new DataUniverse();
universe.AddTable("学生", df);

// 执行 SQL 查询
var 结果 = universe.SqlExecute("SELECT 姓名, 分数 FROM 学生 WHERE 分数 > 15");
```
