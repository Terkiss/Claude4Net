# TeruTeruPandas उपयोगकर्ता गाइड

## अवलोकन
TeruTeruPandas एक हल्का C# DataFrame/Series लाइब्रेरी है, जो pandas से प्रेरित है।
यह Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite), और Compat (pandas शैली के हेल्पर) प्रदान करता है।

## त्वरित शुरुआत
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// एक साधारण DataFrame बनाएं
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["नाम"] = new object[] { "A", "B", "C" },
    ["स्कोर"] = new object[] { 10, 20, 30 }
});

// पहले 2 पंक्तियाँ दिखाएँ
Console.WriteLine(df.Head(2));
```

## नेमस्पेस
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: pandas शैली के एक्सटेंशन मेथड और `Pd` हेल्पर
- `TeruTeruPandas.IO`: CSV/JSON/SQLite इनपुट/आउटपुट

## मुख्य ऑब्जेक्ट्स
### DataFrame
- निर्माण: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- गुण: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- इंडेक्सर:
  - `df["col"]`: कॉलम एक्सेस
  - `df[row, "col"]`: पंक्ति/कॉलम एक्सेस
  - `df[rowKey, "col"]`: लेबल आधारित एक्सेस
  - `df[mask]`: BoolSeries फ़िल्टरिंग
- मुख्य मेथड: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- सांख्यिकी: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- संचयी/परिवर्तन: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: NA सपोर्ट के साथ 1D वेक्टर (`IsNA`, `SetNA`)
- `BoolSeries`: बूलियन मास्क, `&`, `|`, `!` सपोर्ट करता है

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series पोजिशन और लेबल आधारित Index एक्सेस सपोर्ट करते हैं

## इंडेक्सिंग
- पोजिशन: `df.ILoc[row, col]` या `df.Iat(row, col)`
- लेबल: `df.Loc[rowKey, col]` या `df.At(rowKey, col)`
- मास्क: `df[BoolSeries]`

## NA हैंडलिंग
- कॉलम NA मास्क बनाए रखते हैं
- `DropNA()` से NA वाली पंक्तियाँ हटाएँ
- `FillNa(value)` (Compat) से NA को मान से भरें

## समूह और समेकन
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- सपोर्ट: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## मर्ज और संयोजन
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- डुप्लिकेट कॉलम नामों में `_right` जुड़ सकता है

## पिवट और मेल्ट
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## इनपुट/आउटपुट (IO)
### CSV
- पढ़ना: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- लिखना: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- पढ़ना: `JsonIO.ReadJson(path, isJsonLines: false)`
- लिखना: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- पढ़ना: `SqliteIO.ReadSqlite(connectionString, query)`
- लिखना: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- टेबल सूची: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- कई DataFrame को `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable` से प्रबंधित करें
- `Join`, `ConcatTables`, `SqlExecute` से क्वेरी और हेरफेर करें
- पूरे को JSON, डायरेक्टरी (CSV), या SQLite के रूप में सहेजें/पुनर्स्थापित करें

## SQL सपोर्ट
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- सपोर्ट: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- नोट: `ORDER BY` वर्तमान में बिना क्रम के मूल क्रम बनाए रखता है

## सीमाएँ
- टाइप अनुमान सैंपल आधारित है, मुख्यतः string/number/bool के लिए
- `Query` केवल सरल अभिव्यक्तियों (जैसे "col > 5") को सपोर्ट करता है
- `SimplePivot`/`SimpleMelt` बड़े डेटा पर धीमा हो सकता है
- NA हैंडलिंग pandas से भिन्न हो सकती है
