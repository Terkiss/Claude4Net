# Guide de l'utilisateur TeruTeruPandas

## Présentation
TeruTeruPandas est une bibliothèque légère de DataFrame/Series pour C# inspirée de pandas.
Elle fournit Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite) et Compat (helpers de style pandas).

## Démarrage rapide
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

## Espaces de noms
- `TeruTeruPandas.Core` : DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat` : méthodes d'extension style pandas et helper `Pd`
- `TeruTeruPandas.IO` : Entrée/Sortie CSV/JSON/SQLite

## Objets principaux
### DataFrame
- Création : `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Propriétés : `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indexeurs :
  - `df["col"]` : accès colonne
  - `df[row, "col"]` : accès ligne/colonne
  - `df[rowKey, "col"]` : accès par étiquette
  - `df[mask]` : filtrage BoolSeries
- Méthodes principales : `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Statistiques : `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Cumul/variation : `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>` : vecteur 1D avec gestion NA (`IsNA`, `SetNA`)
- `BoolSeries` : masque booléen, supporte `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series supporte accès position/étiquette via Index

## Indexation
- Par position : `df.ILoc[row, col]` ou `df.Iat(row, col)`
- Par étiquette : `df.Loc[rowKey, col]` ou `df.At(rowKey, col)`
- Par masque : `df[BoolSeries]`

## Gestion des NA
- Les colonnes gardent un masque NA
- Supprimer lignes NA : `DropNA()`
- Remplir NA par une valeur : `FillNa(value)` (Compat)

## GroupBy et agrégation
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Supporte : `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Jointures et concaténations
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Les colonnes dupliquées peuvent avoir le suffixe `_right`

## Pivot et melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Entrée/Sortie (IO)
### CSV
- Lire : `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Écrire : `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Lire : `JsonIO.ReadJson(path, isJsonLines: false)`
- Écrire : `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Lire : `SqliteIO.ReadSqlite(connectionString, query)`
- Écrire : `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Lister tables : `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Gère plusieurs DataFrames avec `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Requêtes et manipulations avec `Join`, `ConcatTables`, `SqlExecute`
- Sauvegarde/restaure tout en JSON, dossier (CSV) ou SQLite

## Support SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Supporte : `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Note : `ORDER BY` conserve l'ordre d'origine, pas de tri

## Limitations
- Inférence de type basée sur échantillon, principalement string/number/bool
- `Query` ne supporte que des expressions simples (ex : "col > 5")
- `SimplePivot`/`SimpleMelt` peut être lent sur de gros volumes
- La gestion des NA peut différer de pandas
