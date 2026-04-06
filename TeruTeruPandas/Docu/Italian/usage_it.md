# Guida utente TeruTeruPandas

## Panoramica
TeruTeruPandas è una libreria leggera DataFrame/Series per C# ispirata a pandas.
Fornisce Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite) e Compat (helper in stile pandas).

## Inizio rapido
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Creare un semplice DataFrame
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["nome"] = new object[] { "A", "B", "C" },
    ["punteggio"] = new object[] { 10, 20, 30 }
});

// Visualizza le prime 2 righe
Console.WriteLine(df.Head(2));
```

## Namespace
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: metodi di estensione in stile pandas e helper `Pd`
- `TeruTeruPandas.IO`: Input/Output CSV/JSON/SQLite

## Oggetti principali
### DataFrame
- Creazione: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Proprietà: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indicizzatori:
  - `df["col"]`: accesso alla colonna
  - `df[row, "col"]`: accesso riga/colonna
  - `df[rowKey, "col"]`: accesso basato su etichetta
  - `df[mask]`: filtro con BoolSeries
- Metodi principali: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Statistiche: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Cumulativi/variazioni: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: vettore 1D con supporto NA (`IsNA`, `SetNA`)
- `BoolSeries`: maschera booleana, supporta `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series supportano accesso per posizione ed etichetta tramite Index

## Indicizzazione
- Per posizione: `df.ILoc[row, col]` o `df.Iat(row, col)`
- Per etichetta: `df.Loc[rowKey, col]` o `df.At(rowKey, col)`
- Per maschera: `df[BoolSeries]`

## Gestione NA
- Le colonne mantengono una maschera NA
- Rimuovi le righe con NA usando `DropNA()`
- Riempire NA con un valore usando `FillNa(value)` (Compat)

## Raggruppamento e aggregazione
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Supporta: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Join e concatenazione
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- I nomi delle colonne duplicate possono avere il suffisso `_right`

## Pivot e melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Input/Output (IO)
### CSV
- Lettura: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Scrittura: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Lettura: `JsonIO.ReadJson(path, isJsonLines: false)`
- Scrittura: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Lettura: `SqliteIO.ReadSqlite(connectionString, query)`
- Scrittura: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Elenco tabelle: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Gestisci più DataFrame con `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Interroga e manipola con `Join`, `ConcatTables`, `SqlExecute`
- Salva/ripristina tutto come JSON, directory (CSV) o SQLite

## Supporto SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Supporta: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Nota: `ORDER BY` mantiene l'ordine originale, senza ordinamento

## Limitazioni
- Inferenza del tipo basata su campione, principalmente string/number/bool
- `Query` supporta solo espressioni semplici (es: "col > 5")
- `SimplePivot`/`SimpleMelt` può essere lento con grandi volumi di dati
- La gestione di NA può differire da pandas
