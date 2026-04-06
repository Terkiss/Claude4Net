# Guía del usuario de TeruTeruPandas

## Descripción general
TeruTeruPandas es una biblioteca ligera de DataFrame/Series para C# inspirada en pandas.
Proporciona Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite) y Compat (ayudantes de estilo pandas).

## Inicio rápido
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

## Espacios de nombres
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: métodos de extensión estilo pandas y el ayudante `Pd`
- `TeruTeruPandas.IO`: Entrada/Salida para CSV/JSON/SQLite

## Objetos principales
### DataFrame
- Creación: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Propiedades: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indexadores:
  - `df["col"]`: acceso a columna
  - `df[row, "col"]`: acceso a fila/columna
  - `df[rowKey, "col"]`: acceso basado en etiqueta
  - `df[mask]`: filtrado con BoolSeries
- Métodos principales: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Estadísticas: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Acumulados/cambios: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: vector unidimensional con soporte para NA (`IsNA`, `SetNA`)
- `BoolSeries`: máscara booleana, soporta `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series soportan acceso por posición y etiqueta usando Index

## Indexación
- Por posición: `df.ILoc[row, col]` o `df.Iat(row, col)`
- Por etiqueta: `df.Loc[rowKey, col]` o `df.At(rowKey, col)`
- Por máscara: `df[BoolSeries]`

## Manejo de NA
- Las columnas mantienen una máscara NA
- Elimina filas con NA usando `DropNA()`
- Rellena NA con un valor usando `FillNa(value)` (Compat)

## Agrupación y agregación
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Soporta: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Uniones y concatenaciones
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Los nombres de columna duplicados pueden tener el sufijo `_right`

## Pivot y melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Entrada/Salida (IO)
### CSV
- Leer: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Escribir: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Leer: `JsonIO.ReadJson(path, isJsonLines: false)`
- Escribir: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Leer: `SqliteIO.ReadSqlite(connectionString, query)`
- Escribir: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Listar tablas: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Administra múltiples DataFrames con `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Consulta y manipula con `Join`, `ConcatTables`, `SqlExecute`
- Guarda/restaura todo como JSON, directorio (CSV) o SQLite

## Soporte SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Soporta: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Nota: `ORDER BY` mantiene el orden original, sin ordenamiento

## Limitaciones
- Inferencia de tipos basada en muestras, principalmente string/número/bool
- `Query` solo soporta expresiones simples (ej: "col > 5")
- `SimplePivot`/`SimpleMelt` puede ser lento con grandes volúmenes de datos
- El manejo de NA puede diferir de pandas
