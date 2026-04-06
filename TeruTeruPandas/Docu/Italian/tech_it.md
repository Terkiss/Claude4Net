# Documentazione tecnica TeruTeruPandas

## Architettura
TeruTeruPandas è implementato in C# e si compone dei seguenti moduli principali:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (lettura/scrittura)
- **Compat**: metodi helper in stile pandas
- **DataUniverse**: gestione di più DataFrame, interfaccia SQL, IO massivo

## Classi e interfacce principali
- `DataFrame`: oggetto tabellare principale, supporta indicizzazione, filtraggio, aggregazione, join
- `Series<T>`: vettore 1D con supporto NA
- `Index`: classe base per RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: maschere booleane per il filtraggio
- `DataUniverse`: contenitore di più DataFrame, supporta query SQL e IO massivo

## Parser ed esecuzione SQL
- Supporta query SQL di base: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- Il parser è in DataUniverseSql.cs, l'esecuzione in SqlQueryExecutor
- Limitazioni: solo espressioni semplici, nessuna sottoquery

## Moduli IO
- **CsvReader/CsvWriter**: lettura e scrittura di file CSV
- **JsonIO**: lettura e scrittura di JSON (normale e JSON Lines)
- **SqliteIO**: lettura di tabelle e query SQLite, esportazione di DataFrame in tabella

## Esempi di utilizzo API
```csharp
// Lettura CSV
var df = CsvReader.ReadCsv("data.csv");

// Raggruppamento e aggregazione
var grouped = df.GroupBy("categoria").Agg(new Dictionary<string, string[]> { ["valore"] = new[] { "sum", "mean" } });

// Query SQL
var universe = new DataUniverse();
universe.AddTable("tabella", df);
var result = universe.SqlExecute("SELECT * FROM tabella WHERE valore > 10");
```

## Limitazioni e prestazioni
- Ottimizzato per set di dati piccoli e medi (fino a ~1 milione di righe)
- Non supporta multithreading o calcolo distribuito
- Alcune operazioni (Pivot, Melt, Join complessi) possono essere lente con grandi volumi

## Contributo ed estensione
- Il codice è estendibile: si possono aggiungere nuovi moduli IO, tipi di indice, metodi di aggregazione
- Per l'integrazione con database esterni, usare SqliteIO o implementare un proprio modulo IO
