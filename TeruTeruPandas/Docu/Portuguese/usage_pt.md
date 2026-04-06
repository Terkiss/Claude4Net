# Guia do Usuário TeruTeruPandas

## Visão Geral
TeruTeruPandas é uma biblioteca leve de DataFrame/Series para C# inspirada no pandas.
Oferece Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite) e Compat (ajudantes no estilo pandas).

## Início Rápido
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Criar um DataFrame simples
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["nome"] = new object[] { "A", "B", "C" },
    ["pontuacao"] = new object[] { 10, 20, 30 }
});

// Exibir as 2 primeiras linhas
Console.WriteLine(df.Head(2));
```

## Namespaces
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: métodos de extensão no estilo pandas e o helper `Pd`
- `TeruTeruPandas.IO`: Entrada/Saída CSV/JSON/SQLite

## Objetos Principais
### DataFrame
- Criação: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Propriedades: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Indexadores:
  - `df["col"]`: acesso à coluna
  - `df[row, "col"]`: acesso à linha/coluna
  - `df[rowKey, "col"]`: acesso baseado em rótulo
  - `df[mask]`: filtragem com BoolSeries
- Principais métodos: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Estatísticas: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Acumulados/mudanças: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: vetor 1D com suporte a NA (`IsNA`, `SetNA`)
- `BoolSeries`: máscara booleana, suporta `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series suportam acesso por posição e rótulo usando Index

## Indexação
- Por posição: `df.ILoc[row, col]` ou `df.Iat(row, col)`
- Por rótulo: `df.Loc[rowKey, col]` ou `df.At(rowKey, col)`
- Por máscara: `df[BoolSeries]`

## Tratamento de NA
- As colunas mantêm uma máscara NA
- Remova linhas com NA usando `DropNA()`
- Preencha NA com um valor usando `FillNa(value)` (Compat)

## Agrupamento e agregação
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Suporta: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Junção e concatenação
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Nomes de colunas duplicados podem ter o sufixo `_right`

## Pivot e melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Entrada/Saída (IO)
### CSV
- Ler: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Escrever: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Ler: `JsonIO.ReadJson(path, isJsonLines: false)`
- Escrever: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Ler: `SqliteIO.ReadSqlite(connectionString, query)`
- Escrever: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Listar tabelas: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse
- Gerencie múltiplos DataFrames com `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Consulte e manipule com `Join`, `ConcatTables`, `SqlExecute`
- Salve/restaure tudo como JSON, diretório (CSV) ou SQLite

## Suporte a SQL
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Suporta: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Nota: `ORDER BY` mantém a ordem original, sem ordenação

## Limitações
- Inferência de tipo baseada em amostra, principalmente string/number/bool
- `Query` suporta apenas expressões simples (ex: "col > 5")
- `SimplePivot`/`SimpleMelt` pode ser lento com grandes volumes de dados
- O tratamento de NA pode diferir do pandas
