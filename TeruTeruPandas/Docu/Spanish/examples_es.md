# Ejemplos de uso de TeruTeruPandas

## Ejemplo básico de DataFrame
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

// Crear un DataFrame simple
dynamic df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["nombre"] = new object[] { "A", "B", "C" },
    ["puntuación"] = new object[] { 10, 20, 30 }
});

// Mostrar las primeras 2 filas
Console.WriteLine(df.Head(2));
```

## Leer y escribir CSV
```csharp
using TeruTeruPandas.IO;

// Leer un archivo CSV
dynamic df = CsvReader.ReadCsv("datos.csv", hasHeader: true);

// Guardar el DataFrame como CSV
CsvWriter.ToCsv(df, "salida.csv");
```

## Consultar con SQL en DataUniverse
```csharp
using TeruTeruPandas.Core;

// Crear un DataUniverse y agregar tablas
dynamic universe = new DataUniverse();
universe.AddTable("alumnos", df);

// Ejecutar una consulta SQL
var resultado = universe.SqlExecute("SELECT nombre, puntuación FROM alumnos WHERE puntuación > 15");
```
