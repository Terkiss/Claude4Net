# دليل مستخدم TeruTeruPandas

## نظرة عامة
TeruTeruPandas هي مكتبة DataFrame/Series خفيفة الوزن للغة C# مستوحاة من pandas.
توفر Core (DataFrame, Series, Index)، وIO (CSV/JSON/SQLite)، وCompat (مساعدات بأسلوب pandas).

## البدء السريع
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// إنشاء DataFrame بسيط
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["الاسم"] = new object[] { "A", "B", "C" },
    ["الدرجة"] = new object[] { 10, 20, 30 }
});

// عرض أول صفين
Console.WriteLine(df.Head(2));
```

## المساحات الاسمية
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: طرق تمديد بأسلوب pandas ومساعد `Pd`
- `TeruTeruPandas.IO`: إدخال/إخراج CSV/JSON/SQLite

## الكائنات الأساسية
### DataFrame
- الإنشاء: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- الخصائص: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- الفهارس:
  - `df["col"]`: الوصول إلى العمود
  - `df[row, "col"]`: الوصول إلى الصف/العمود
  - `df[rowKey, "col"]`: الوصول حسب التسمية
  - `df[mask]`: تصفية باستخدام BoolSeries
- الطرق الرئيسية: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- الإحصائيات: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- التراكم/التغير: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: متجه أحادي البعد يدعم NA (`IsNA`, `SetNA`)
- `BoolSeries`: قناع منطقي يدعم `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series يدعم الوصول حسب الموقع أو التسمية باستخدام Index

## الفهرسة
- حسب الموقع: `df.ILoc[row, col]` أو `df.Iat(row, col)`
- حسب التسمية: `df.Loc[rowKey, col]` أو `df.At(rowKey, col)`
- حسب القناع: `df[BoolSeries]`

## معالجة NA
- الأعمدة تحتفظ بقناع NA
- إزالة الصفوف التي تحتوي على NA باستخدام `DropNA()`
- ملء NA بقيمة باستخدام `FillNa(value)` (Compat)

## التجميع والتجميع الإحصائي
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- يدعم: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## الدمج والربط
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- قد يتم إلحاق `_right` بأسماء الأعمدة المكررة

## Pivot و Melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## الإدخال/الإخراج (IO)
### CSV
- القراءة: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- الكتابة: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- القراءة: `JsonIO.ReadJson(path, isJsonLines: false)`
- الكتابة: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- القراءة: `SqliteIO.ReadSqlite(connectionString, query)`
- الكتابة: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- قائمة الجداول: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- إدارة عدة DataFrame باستخدام `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- الاستعلام والتعديل باستخدام `Join`, `ConcatTables`, `SqlExecute`
- حفظ/استعادة الكل كـ JSON أو مجلد (CSV) أو SQLite

## دعم SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- يدعم: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- ملاحظة: `ORDER BY` يحافظ على الترتيب الأصلي بدون فرز

## القيود
- استنتاج النوع يعتمد على العينة، ويعمل بشكل أساسي مع string/number/bool
- `Query` يدعم فقط التعبيرات البسيطة (مثال: "col > 5")
- `SimplePivot`/`SimpleMelt` قد يكون بطيئًا مع البيانات الكبيرة
- معالجة NA قد تختلف عن pandas
