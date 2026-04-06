# TeruTeruPandas Anwendungsbeispiele

## Einfaches DataFrame-Beispiel
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Einfaches DataFrame erstellen
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["name"] = new object[] { "A", "B", "C" },
    ["punktzahl"] = new object[] { 10, 20, 30 }
});

// Die ersten 2 Zeilen anzeigen
Console.WriteLine(df.Head(2));
```

## CSV lesen und schreiben
```csharp
using TeruTeruPandas.IO;

// CSV-Datei lesen
dynamic df = CsvReader.ReadCsv("daten.csv", hasHeader: true);

// DataFrame als CSV speichern
CsvWriter.ToCsv(df, "ausgabe.csv");
```

## SQL-Abfrage mit DataUniverse
```csharp
using TeruTeruPandas.Core;

// DataUniverse erstellen und Tabellen hinzufügen
dynamic universe = new DataUniverse();
universe.AddTable("schueler", df);

// SQL-Abfrage ausführen
var ergebnis = universe.SqlExecute("SELECT name, punktzahl FROM schueler WHERE punktzahl > 15");
```
