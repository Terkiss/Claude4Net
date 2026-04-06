# 테루테루판다스 기술 문서

## 아키텍처
TeruTeruPandas는 C#으로 구현되었으며, 주요 모듈은 다음과 같습니다:
- **Core**: DataFrame, Series, Index, GroupBy, Join, Pivot, Melt
- **IO**: CSV, JSON, SQLite (입출력)
- **Compat**: pandas 스타일의 헬퍼 메서드
- **DataUniverse**: 다수의 DataFrame 관리, SQL 인터페이스, 대량 IO

## 주요 클래스 및 인터페이스
- `DataFrame`: 표 형식의 핵심 객체. 인덱싱, 필터링, 집계, 병합 지원
- `Series<T>`: NA 처리가 가능한 1차원 벡터
- `Index`: RangeIndex, IntIndex, StringIndex, DateTimeIndex의 기반 클래스
- `BoolSeries`: 필터링용 불리언 마스크
- `DataUniverse`: 여러 DataFrame을 담는 컨테이너. SQL 쿼리 및 대량 IO 지원

## SQL 파서 및 실행
- 기본 SQL 쿼리(SELECT, WHERE, JOIN, GROUP BY, ORDER BY, LIMIT) 지원
- 파서는 DataUniverseSql.cs, 실행은 SqlQueryExecutor에서 구현
- 제한: 단순 표현식만 지원, 서브쿼리 미지원

## IO 모듈
- **CsvReader/CsvWriter**: CSV 파일 읽기/쓰기
- **JsonIO**: JSON(일반/JSON Lines) 읽기/쓰기
- **SqliteIO**: SQLite 테이블 읽기, 쿼리 실행, DataFrame을 테이블로 내보내기

## API 사용 예시
```csharp
// CSV 읽기
var df = CsvReader.ReadCsv("data.csv");

// 그룹화 및 집계
var grouped = df.GroupBy("카테고리").Agg(new Dictionary<string, string[]> { ["값"] = new[] { "sum", "mean" } });

// SQL 쿼리
var universe = new DataUniverse();
universe.AddTable("테이블", df);
var result = universe.SqlExecute("SELECT * FROM 테이블 WHERE 값 > 10");
```

## 제한사항 및 성능
- 소~중규모 데이터셋(약 100만 행 이하)에 최적화
- 멀티스레드, 분산처리 미지원
- 일부 연산(Pivot, Melt, 복잡한 Join)은 대용량 데이터에서 느릴 수 있음

## 확장 및 기여
- 코드 확장 가능: 새로운 IO 모듈, 인덱스 타입, 집계 메서드 추가 가능
- 외부 DB 연동은 SqliteIO 또는 직접 IO 모듈 구현으로 지원
