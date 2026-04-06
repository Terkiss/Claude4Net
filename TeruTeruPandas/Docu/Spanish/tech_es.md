# Documentación técnica de TeruTeruPandas

## Arquitectura
TeruTeruPandas está implementado en C# y se compone de los siguientes módulos principales:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (lectura/escritura)
- **Compat**: métodos auxiliares al estilo pandas
- **DataUniverse**: gestión de múltiples DataFrames, interfaz SQL, IO masivo

## Clases e interfaces principales
- `DataFrame`: objeto tabular principal, soporta indexación, filtrado, agregación, unión
- `Series<T>`: vector unidimensional con soporte para NA
- `Index`: clase base para RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: máscaras booleanas para filtrado
- `DataUniverse`: contenedor de múltiples DataFrames, soporta consultas SQL y IO masivo

## Analizador y ejecución SQL
- Soporta consultas SQL básicas: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- El parser está en DataUniverseSql.cs, la ejecución en SqlQueryExecutor
- Limitaciones: solo expresiones simples, no hay subconsultas

## Módulos IO
- **CsvReader/CsvWriter**: lectura y escritura de archivos CSV
- **JsonIO**: lectura y escritura de JSON (normal y JSON Lines)
- **SqliteIO**: lectura de tablas y consultas a SQLite, exportación de DataFrame a tabla

## Ejemplos de uso de la API
```csharp
// Leer CSV
var df = CsvReader.ReadCsv("data.csv");

// Agrupación y agregación
var grouped = df.GroupBy("categoría").Agg(new Dictionary<string, string[]> { ["valor"] = new[] { "sum", "mean" } });

// Consulta SQL
var universe = new DataUniverse();
universe.AddTable("tabla", df);
var result = universe.SqlExecute("SELECT * FROM tabla WHERE valor > 10");
```

## Limitaciones y rendimiento
- Optimizado para conjuntos de datos pequeños y medianos (hasta ~1 millón de filas)
- No soporta multihilo ni procesamiento distribuido
- Algunas operaciones (Pivot, Melt, Join complejos) pueden ser lentas con grandes volúmenes

## Contribución y extensión
- El código es extensible: se pueden añadir nuevos módulos IO, tipos de índice, métodos de agregación
- Para integración con bases de datos externas, use SqliteIO o implemente su propio módulo IO
