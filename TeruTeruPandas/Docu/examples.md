# TeruTeruPandas Examples

## 1) Create a DataFrame
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

var df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["user_id"] = new object[] { 1, 2, 3 },
    ["name"] = new object[] { "Alice", "Bob", "Cara" },
    ["age"] = new object[] { 25, 31, 29 }
});

Console.WriteLine(df);
```

## 2) Indexing and selection
```csharp
// Position-based
var age00 = df.Iat(0, 2);
df.ILoc[1, 1] = "Bobby";

// Label-based (using RangeIndex keys)
var ageRow1 = df.At(1, "age");
df.SetAt(2, "age", 30);
```

## 3) Boolean filtering
```csharp
var mask = df["age"].Compare(28, (a, b) => (int)a > b);
var adults = df[mask];
Console.WriteLine(adults);
```

## 4) GroupBy + aggregation
```csharp
var sales = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["region"] = new object[] { "East", "West", "East", "West" },
    ["sales"] = new object[] { 100, 150, 120, 180 }
});

var grouped = sales.GroupBy("region").Agg(new Dictionary<string, string[]>
{
    ["sales"] = new[] { "sum", "mean" }
});

Console.WriteLine(new DataFrame(grouped));
```

## 5) Merge (join)
```csharp
var users = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["user_id"] = new object[] { 1, 2, 3 },
    ["name"] = new object[] { "Alice", "Bob", "Cara" }
});

var orders = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["order_id"] = new object[] { 101, 102, 103 },
    ["user_id"] = new object[] { 1, 2, 2 }
});

var joined = users.Merge(orders, "user_id", "inner");
Console.WriteLine(joined);
```

## 6) Concat rows and columns
```csharp
var df1 = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2 },
    ["score"] = new object[] { 10, 20 }
});

var df2 = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 3, 4 },
    ["score"] = new object[] { 30, 40 }
});

var stacked = DataFrameJoinExtensions.Concat(new[] { df1, df2 }, axis: 0);
var sideBySide = DataFrameJoinExtensions.Concat(new[] { df1, df2 }, axis: 1);
```

## 7) Pivot and melt
```csharp
var longDf = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["day"] = new object[] { "Mon", "Mon", "Tue", "Tue" },
    ["type"] = new object[] { "A", "B", "A", "B" },
    ["value"] = new object[] { 10, 20, 15, 25 }
});

var pivot = longDf.SimplePivot("day", "type", "value");
var melt = pivot.SimpleMelt(new[] { "day" }, new[] { "value_A", "value_B" });
```

## 8) CSV / JSON IO
```csharp
df.ToCsv("users.csv");
df.ToJson("users.json", pretty: true);

var fromCsv = Pd.ReadCsv("users.csv");
var fromJson = Pd.ReadJson("users.json");
```

## 9) DataUniverse + SQL
```csharp
var universe = new DataUniverse();
universe.AddTable("users", users);
universe.AddTable("orders", orders);

var result = universe.SqlExecute(
    "SELECT users.name, orders.order_id FROM users " +
    "INNER JOIN orders ON users.user_id = orders.user_id " +
    "WHERE orders.order_id > 101 LIMIT 5"
);
```
