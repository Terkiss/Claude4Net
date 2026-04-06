# Documentation technique TeruTeruPandas

## Architecture
TeruTeruPandas est implémenté en C# et se compose des modules principaux suivants :
- **Core** : DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO** : CSV, JSON, SQLite (lecture/écriture)
- **Compat** : méthodes utilitaires à la pandas
- **DataUniverse** : gestion de plusieurs DataFrames, interface SQL, IO massif

## Classes et interfaces principales
- `DataFrame` : objet tabulaire principal, prend en charge l'indexation, le filtrage, l'agrégation, la jointure
- `Series<T>` : vecteur 1D avec gestion NA
- `Index` : classe de base pour RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries` : masques booléens pour le filtrage
- `DataUniverse` : conteneur de plusieurs DataFrames, supporte les requêtes SQL et l'IO massif

## Analyseur SQL et exécution
- Prend en charge les requêtes SQL de base : SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- Le parser est dans DataUniverseSql.cs, l'exécution dans SqlQueryExecutor
- Limitations : expressions simples uniquement, pas de sous-requêtes

## Modules IO
- **CsvReader/CsvWriter** : lecture et écriture de fichiers CSV
- **JsonIO** : lecture et écriture de JSON (normal et JSON Lines)
- **SqliteIO** : lecture de tables et requêtes SQLite, exportation de DataFrame en table

## Exemples d'utilisation de l'API
```csharp
// Lire un CSV
var df = CsvReader.ReadCsv("data.csv");

// GroupBy et agrégation
var grouped = df.GroupBy("catégorie").Agg(new Dictionary<string, string[]> { ["valeur"] = new[] { "sum", "mean" } });

// Requête SQL
var universe = new DataUniverse();
universe.AddTable("table", df);
var result = universe.SqlExecute("SELECT * FROM table WHERE valeur > 10");
```

## Limitations et performances
- Optimisé pour des jeux de données petits à moyens (jusqu'à ~1 million de lignes)
- Pas de support du multithreading ou du calcul distribué
- Certaines opérations (Pivot, Melt, jointures complexes) peuvent être lentes sur de gros volumes

## Contribution et extension
- Le code est extensible : nouveaux modules IO, types d'index, méthodes d'agrégation peuvent être ajoutés
- Pour l'intégration avec des bases externes, utilisez SqliteIO ou implémentez votre propre module IO
