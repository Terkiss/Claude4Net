# TeruTeruPandas Benutzerhandbuch

## Übersicht
TeruTeruPandas ist eine leichtgewichtige DataFrame/Series-Bibliothek für C#, inspiriert von pandas.
Bietet Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite) und Kompatibilität (pandas-ähnliche Helfer).

## Schnellstart
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
- `TeruTeruPandas.Compat`: pandas-ähnliche Erweiterungsmethoden und `Pd`-Helfer
- `TeruTeruPandas.IO`: CSV/JSON/SQLite Ein-/Ausgabe

## Hauptobjekte
### DataFrame
- Erstellung: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Eigenschaften: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indexer:
  - `df["col"]`: Spaltenzugriff
  - `df[row, "col"]`: Zeilen-/Spaltenzugriff
  - `df[rowKey, "col"]`: Label-basierter Zugriff
  - `df[mask]`: BoolSeries-Filterung
- Hauptmethoden: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Statistik: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Kumulativ/Veränderung: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: 1D-Vektor mit NA-Unterstützung (`IsNA`, `SetNA`)
- `BoolSeries`: Boolesche Maske, unterstützt `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series unterstützen positions- und labelbasierten Zugriff über Index

## Indizierung
- Position: `df.ILoc[row, col]` oder `df.Iat(row, col)`
- Label: `df.Loc[rowKey, col]` oder `df.At(rowKey, col)`
- Maske: `df[BoolSeries]`

## NA-Behandlung
- Spalten behalten NA-Maske
- Entferne NA-Zeilen mit `DropNA()`
- Fülle NA mit Wert mit `FillNa(value)` (Compat)

## Gruppierung und Aggregation
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Unterstützt: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Join und Verkettung
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Doppelte Spaltennamen können das Suffix `_right` erhalten

## Pivot und Melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Ein-/Ausgabe (IO)
### CSV
- Lesen: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Schreiben: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Lesen: `JsonIO.ReadJson(path, isJsonLines: false)`
- Schreiben: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Lesen: `SqliteIO.ReadSqlite(connectionString, query)`
- Schreiben: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Tabellennamen auflisten: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Verwalte mehrere DataFrames mit `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Abfrage und Manipulation mit `Join`, `ConcatTables`, `SqlExecute`
- Alles als JSON, Verzeichnis (CSV) oder SQLite speichern/wiederherstellen

## SQL-Unterstützung
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Unterstützt: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Hinweis: `ORDER BY` behält Originalreihenfolge, keine Sortierung

## Einschränkungen
- Typinferenz basiert auf Stichproben, hauptsächlich string/number/bool
- `Query` unterstützt nur einfache Ausdrücke (z.B. "col > 5")
- `SimplePivot`/`SimpleMelt` kann bei großen Datenmengen langsam sein
- NA-Behandlung kann sich von pandas unterscheiden
