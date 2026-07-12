# PandasDbTool Execution Protocol

## 1. Safety First
- 데이터를 삭제하거나 업데이트하기 전에는 반드시 `pandas_query`로 대상 데이터를 먼저 확인(SELECT)한다.
- 중요한 대규모 변경 전에는 `pandas_snapshot`으로 백업을 생성하는 것을 권장한다.

## 2. RAG Best Practices
- `agent_memory`에 저장할 때는 핵심 키워드를 반드시 포함하여 검색 효율을 높인다.
- 임베딩 데이터는 항상 최신 모델 기준으로 정규화하여 저장한다.

## 3. Query Efficiency
- 복잡한 조인 연산보다는 가능한 한 단일 테이블 쿼리나 에이전트 측에서의 후처리를 우선 고려한다.
