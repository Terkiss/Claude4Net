# Documentação Técnica TeruTeruPandas

## Arquitetura
TeruTeruPandas é implementado em C# e possui os seguintes módulos principais:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (leitura/escrita)
- **Compat**: métodos auxiliares no estilo pandas
- **DataUniverse**: gerenciamento de múltiplos DataFrames, interface SQL, IO em massa

## Principais classes e interfaces
- `DataFrame`: objeto tabular principal, suporta indexação, filtragem, agregação, junção
- `Series<T>`: vetor 1D com suporte a NA
- `Index`: classe base para RangeIndex, IntIndex, StringIndex, DateTimeIndex
- `BoolSeries`: máscaras booleanas para filtragem
- `DataUniverse`: contêiner de múltiplos DataFrames, suporta consultas SQL e IO em massa

## Parser e execução SQL
- Suporta consultas SQL básicas: SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT
- O parser está em DataUniverseSql.cs, execução em SqlQueryExecutor
- Limitações: apenas expressões simples, sem subconsultas

## Módulos IO
- **CsvReader/CsvWriter**: leitura e escrita de arquivos CSV
- **JsonIO**: leitura e escrita de JSON (normal e JSON Lines)
- **SqliteIO**: leitura de tabelas e consultas SQLite, exportação de DataFrame para tabela

## Exemplos de uso da API
```csharp
// Ler CSV
var df = CsvReader.ReadCsv("data.csv");

// Agrupamento e agregação
var grouped = df.GroupBy("categoria").Agg(new Dictionary<string, string[]> { ["valor"] = new[] { "sum", "mean" } });

// Consulta SQL
var universe = new DataUniverse();
universe.AddTable("tabela", df);
var result = universe.SqlExecute("SELECT * FROM tabela WHERE valor > 10");
```

## Limitações e desempenho
- Otimizado para conjuntos de dados pequenos e médios (até ~1 milhão de linhas)
- Não suporta multithreading ou computação distribuída
- Algumas operações (Pivot, Melt, Join complexos) podem ser lentas com grandes volumes

## Contribuição e extensão
- O código é extensível: novos módulos IO, tipos de índice, métodos de agregação podem ser adicionados
- Para integração com bancos de dados externos, use SqliteIO ou implemente seu próprio módulo IO
