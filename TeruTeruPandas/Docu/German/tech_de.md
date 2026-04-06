# TeruTeruPandas Technische Dokumentation

## Architektur
TeruTeruPandas ist in C# implementiert und besteht aus folgenden Hauptmodulen:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (Lesen/Schreiben)
- **Compat**: Hilfsmethoden im pandas-Stil
- **DataUniverse**: Verwaltung mehrerer DataFrames, SQL-Interface, Massen-IO

## Wichtige Klassen und Schnittstellen
- `DataFrame`: Haupttabellenobjekt, unterstützt Indizierung, Filterung, Aggregation, Join
- `Series<T>`: 1D-Vektor mit NA-Unterstützung
- `Index`: Basisklasse für RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: Boolesche Masken für Filterung
- `DataUniverse`: Container für mehrere DataFrames, unterstützt SQL-Abfragen und Massen-IO

## SQL-Parser und Ausführung
- Unterstützt grundlegende SQL-Abfragen: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- Parser in DataUniverseSql.cs, Ausführung über SqlQueryExecutor
- Einschränkungen: nur einfache Ausdrücke, keine Subqueries

## IO-Module
- **CsvReader/CsvWriter**: Lesen und Schreiben von CSV-Dateien
- **JsonIO**: Lesen und Schreiben von JSON (normal und JSON Lines)
- **SqliteIO**: Lesen von Tabellen und Abfragen in SQLite, Export von DataFrame in Tabelle

## API-Beispiele
```csharp
// CSV lesen
var df = CsvReader.ReadCsv("data.csv");

// Gruppierung und Aggregation
var grouped = df.GroupBy("Kategorie").Agg(new Dictionary<string, string[]> { ["Wert"] = new[] { "sum", "mean" } });

// SQL-Abfrage
var universe = new DataUniverse();
universe.AddTable("Tabelle", df);
var result = universe.SqlExecute("SELECT * FROM Tabelle WHERE Wert > 10");
```

## Einschränkungen und Performance
- Optimiert für kleine bis mittlere Datensätze (bis ~1 Mio. Zeilen)
- Kein Multithreading oder verteiltes Rechnen
- Einige Operationen (Pivot, Melt, komplexe Joins) können bei großen Datenmengen langsam sein

## Erweiterung und Beitrag
- Der Code ist erweiterbar: Neue IO-Module, Indextypen, Aggregationsmethoden können hinzugefügt werden
- Für die Integration mit externen Datenbanken nutzen Sie SqliteIO oder implementieren Sie ein eigenes IO-Modul
