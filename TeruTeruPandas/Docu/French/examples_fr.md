# Exemples d'utilisation de TeruTeruPandas

## Exemple de base DataFrame
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Créer un DataFrame simple
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["nom"] = new object[] { "A", "B", "C" },
    ["score"] = new object[] { 10, 20, 30 }
});

// Afficher les 2 premières lignes
Console.WriteLine(df.Head(2));
```

## Lecture et écriture CSV
```csharp
using TeruTeruPandas.IO;

// Lire un fichier CSV
dynamic df = CsvReader.ReadCsv("donnees.csv", hasHeader: true);

// Sauvegarder le DataFrame en CSV
CsvWriter.ToCsv(df, "sortie.csv");
```

## Requête SQL avec DataUniverse
```csharp
using TeruTeruPandas.Core;

// Créer un DataUniverse et ajouter des tables
dynamic universe = new DataUniverse();
universe.AddTable("eleves", df);

// Exécuter une requête SQL
var resultat = universe.SqlExecute("SELECT nom, score FROM eleves WHERE score > 15");
```
