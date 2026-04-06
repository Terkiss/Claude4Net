# Руководство пользователя TeruTeruPandas

## Обзор
TeruTeruPandas — это легковесная библиотека DataFrame/Series для C#, вдохновленная pandas.
Включает Core (DataFrame, Series, Index), IO (CSV/JSON/SQLite), Compat (утилиты в стиле pandas).

## Быстрый старт
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

## Пространства имен
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: расширения в стиле pandas и помощник `Pd`
- `TeruTeruPandas.IO`: ввод/вывод CSV/JSON/SQLite

## Основные объекты
### DataFrame
- Создание: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- Свойства: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- Индексаторы:
  - `df["col"]`: доступ к столбцу
  - `df[row, "col"]`: доступ к строке/столбцу
  - `df[rowKey, "col"]`: доступ по метке
  - `df[mask]`: фильтрация с помощью BoolSeries
- Основные методы: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- Статистика: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- Кумулятивные/изменения: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: одномерный вектор с поддержкой NA (`IsNA`, `SetNA`)
- `BoolSeries`: булевы маски, поддержка `&`, `|`, `!`

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series поддерживают позиционный и меточный доступ через Index

## Индексация
- По позиции: `df.ILoc[row, col]` или `df.Iat(row, col)`
- По метке: `df.Loc[rowKey, col]` или `df.At(rowKey, col)`
- По маске: `df[BoolSeries]`

## Работа с NA
- Столбцы хранят маски NA
- `DropNA()` удаляет строки с NA
- `FillNa(value)` (Compat) заменяет NA на константу

## Группировка и агрегация
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- Поддержка: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## Объединение и конкатенация
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- Дублирующиеся имена столбцов могут получить суффикс `_right`.

## Пивот и melt
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## Ввод/вывод (IO)
### CSV
- Чтение: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- Запись: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- Чтение: `JsonIO.ReadJson(path, isJsonLines: false)`
- Запись: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- Чтение: `SqliteIO.ReadSqlite(connectionString, query)`
- Запись: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- Список таблиц: `SqliteIO.GetTableNames(dbPath)`

## DataUniverse (Данные Вселенной)
- Управление несколькими DataFrame с помощью `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`
- Запросы с помощью `Join`, `ConcatTables`, `SqlExecute`
- Сохранение/восстановление всей вселенной в JSON, директорию (CSV) или SQLite

## SQL поддержка
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- Поддержка: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- Примечание: `ORDER BY` пока не сортирует, сохраняет исходный порядок

## Ограничения
- Определение типа основано на выборке, преимущественно string/number/bool
- `Query` поддерживает только простые выражения (например, "col > 5")
- `SimplePivot`/`SimpleMelt` могут быть медленными на больших данных
- Работа с NA может отличаться от pandas в некоторых случаях
