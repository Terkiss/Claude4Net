# PandasDbTool Error Playbook

## 1. Table Not Found
- **Scenario**: エージェントが存在しないテーブルにクエリを実行。
- **Action**: `pandas_list_tables` を実行して、存在するテーブル名を再確認する。

## 2. Column Dimension Mismatch
- **Scenario**: 임베딩 저장 시 차원 불일치 발생.
- **Action**: 현재 에이전트가 사용하는 임베딩 모델의 차원(예: 768)과 테이블의 기존 차원을 비교하고, 필요시 테이블을 재생성하거나 데이터를 클렌징한다.

## 3. Transaction Queue Timeout
- **Scenario**: DB 작업이 너무 오래 걸림.
- **Action**: 작업을 소분하여 재시도하거나, `!doctor` 명령으로 DB 연결 상태를 확인한다.
