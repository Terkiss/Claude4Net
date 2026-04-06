# Примеры использования TeruTeruPandas

## Базовый пример DataFrame
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Создание простого DataFrame
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["имя"] = new object[] { "A", "B", "C" },
    ["оценка"] = new object[] { 10, 20, 30 }
});

// Показать первые 2 строки
Console.WriteLine(df.Head(2));
```

## Чтение и запись CSV
```csharp
using TeruTeruPandas.IO;

// Прочитать CSV-файл
dynamic df = CsvReader.ReadCsv("data.csv", hasHeader: true);

// Сохранить DataFrame как CSV
CsvWriter.ToCsv(df, "output.csv");
```

## SQL-запрос в DataUniverse
```csharp
using TeruTeruPandas.Core;

// Создать DataUniverse и добавить таблицу
dynamic universe = new DataUniverse();
universe.AddTable("студенты", df);

// Выполнить SQL-запрос
var результат = universe.SqlExecute("SELECT имя, оценка FROM студенты WHERE оценка > 15");
```
