# 테루테루판다스 사용자 가이드

## 개요
TeruTeruPandas는 pandas에서 영감을 받은 경량 C# DataFrame/Series 라이브러리입니다.
Core(DataFrame, Series, Index), IO(CSV/JSON/SQLite), Compat(pandas 스타일 헬퍼)를 제공합니다.

## 빠른 시작
```csharp
using TeruTeruPandas.Core;
using TeruTeruPandas.Compat;

var df = Pd.DataFrame(new Dictionary<string, object[]>
{
    ["id"] = new object[] { 1, 2, 3 },
    ["name"] = new object[] { "A", "B", "C" },
    ["score"] = new object[] { 10, 20, 30 }
});

Console.WriteLine(df.Head(2));
```

## 네임스페이스
- `TeruTeruPandas.Core`: DataFrame, Series, Index, Join/Pivot, GroupBy
- `TeruTeruPandas.Compat`: pandas 스타일 확장 메서드 및 `Pd` 헬퍼
- `TeruTeruPandas.IO`: CSV/JSON/SQLite 입출력

## 핵심 객체
### DataFrame
- 생성: `new DataFrame(Dictionary<string, IColumn> columns, Index? index = null)`
- 속성: `RowCount`, `ColumnCount`, `Columns`, `Index`, `Values`, `Dtypes`
- 인덱서:
  - `df["col"]`: 컬럼 접근
  - `df[row, "col"]`: 행/컬럼 접근
  - `df[rowKey, "col"]`: 라벨 기반 접근
  - `df[mask]`: BoolSeries 필터링
- 주요 메서드: `Head`, `Tail`, `DropNA`, `Info`, `Describe`, `IsNa`, `NotNa`
- 통계: `Std`, `Var`, `Median`, `Min`, `Max`, `Quantile`
- 누적/변화: `Cumsum`, `Cumprod`, `Cummax`, `Cummin`, `Diff`, `PctChange`

### Series<T> / BoolSeries
- `Series<T>`: NA 처리가 가능한 1차원 벡터 (`IsNA`, `SetNA`)
- `BoolSeries`: 불리언 마스크, `&`, `|`, `!` 지원

### Index
- `RangeIndex`, `IntIndex`, `StringIndex`, `DateTimeIndex`
- DataFrame/Series는 위치 및 라벨 기반 접근을 Index로 지원

## 인덱싱
- 위치: `df.ILoc[row, col]` 또는 `df.Iat(row, col)`
- 라벨: `df.Loc[rowKey, col]` 또는 `df.At(rowKey, col)`
- 마스크: `df[BoolSeries]`

## NA 처리
- 컬럼은 NA 마스크를 유지
- `DropNA()`로 NA가 포함된 행 제거
- `FillNa(value)`(Compat)로 NA를 상수로 채움

## 그룹화 및 집계
- `df.GroupBy("col").Agg(new Dictionary<string, string[]>{ ... })`
- 지원: `sum`, `mean`, `count`, `max`, `min`, `std`, `var`

## 조인과 연결
- `df.Merge(other, on: "key", how: "inner|left|right|outer", strategy: JoinStrategy.Auto|Hash|Index|NestedLoop)`
- `DataFrameJoinExtensions.Concat(dataframes, axis: 0|1)`
- 중복 컬럼명은 `_right` 접미사가 붙을 수 있습니다.

## 피벗과 멜트
- `df.SimplePivot(indexCol, columnCol, valueCol)`
- `df.SimpleMelt(idVars, valueVars, varName, valueName)`

## 입출력(IO)
### CSV
- 읽기: `CsvReader.ReadCsv(path, hasHeader: true, separator: ',')`
- 쓰기: `CsvWriter.ToCsv(df, path, includeHeader: true)`

### JSON
- 읽기: `JsonIO.ReadJson(path, isJsonLines: false)`
- 쓰기: `JsonIO.ToJson(df, path, pretty: false, asJsonLines: false)`

### SQLite
- 읽기: `SqliteIO.ReadSqlite(connectionString, query)`
- 쓰기: `SqliteIO.ToSqlite(df, connectionString, tableName, ifExists: false)`
- 테이블 목록: `SqliteIO.GetTableNames(dbPath)`

## 데이터유니버스(DataUniverse)
- 여러 DataFrame을 `AddTable`, `UpdateTable`, `GetTable`, `RemoveTable`로 관리
- `Join`, `ConcatTables`, `SqlExecute`로 쿼리 및 조작
- 전체를 JSON, 디렉터리(CSV), SQLite로 저장/복원 가능

## SQL 지원
- `universe.SqlExecute("SELECT ... FROM ... WHERE ...")`
- 지원: `SELECT`, `WHERE`, `JOIN`, `GROUP BY`, `ORDER BY`, `LIMIT`
- 참고: `ORDER BY`는 현재 정렬 없이 원본 순서 유지

## 제한사항
- 타입 추론은 샘플 기반이며 string/number/bool 위주로 동작
- `Query`는 단순 표현식(예: "col > 5")만 지원
- `SimplePivot`/`SimpleMelt`는 대용량 데이터에서 느릴 수 있음
- NA 처리 방식이 pandas와 일부 다를 수 있음
