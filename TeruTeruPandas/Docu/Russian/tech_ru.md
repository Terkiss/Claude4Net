# Техническая документация TeruTeruPandas

## Архитектура
TeruTeruPandas реализован на C# и состоит из следующих основных модулей:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (чтение/запись)
- **Compat**: вспомогательные методы в стиле pandas
- **DataUniverse**: управление множеством DataFrame, SQL-интерфейс, массовый IO

## Основные классы и интерфейсы
- `DataFrame`: основной табличный объект, поддерживает индексацию, фильтрацию, агрегацию, объединение
- `Series<T>`: одномерный вектор с поддержкой NA
- `Index`: базовый класс для RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: булевы маски для фильтрации
- `DataUniverse`: контейнер для множества DataFrame, поддерживает SQL-запросы и массовый IO

## SQL-парсер и выполнение
- Поддерживает базовые SQL-запросы: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- Парсер реализован в DataUniverseSql.cs, выполнение — через SqlQueryExecutor
- Ограничения: только простые выражения, нет вложенных запросов

## IO-модули
- **CsvReader/CsvWriter**: чтение и запись CSV-файлов
- **JsonIO**: чтение и запись JSON (обычный и JSON Lines)
- **SqliteIO**: чтение таблиц и выполнение запросов к SQLite, экспорт DataFrame в таблицу

## Примеры использования API
```csharp
// Чтение CSV
var df = CsvReader.ReadCsv("data.csv");

// Группировка и агрегация
var grouped = df.GroupBy("категория").Agg(new Dictionary<string, string[]> { ["значение"] = new[] { "sum", "mean" } });

// SQL-запрос
var universe = new DataUniverse();
universe.AddTable("таблица", df);
var result = universe.SqlExecute("SELECT * FROM таблица WHERE значение > 10");
```

## Ограничения и производительность
- Оптимизировано для небольших и средних наборов данных (до ~1 млн строк)
- Нет поддержки многопоточности и распределённых вычислений
- Некоторые операции (Pivot, Melt, сложные Join) могут быть медленными на больших данных

## Вклад и расширение
- Код открыт для расширения: можно добавлять новые IO-модули, типы индексов, методы агрегации
- Для интеграции с внешними БД используйте SqliteIO или реализуйте собственный IO-модуль
