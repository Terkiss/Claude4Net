# Claude4Net 차세대 엔지니어링 관제 대시보드 & TeruTeruPandas 캘린더 통합 마스터 설계서
**Architecture Decision Record & Master Specification (ADR-2026-001 v3.0 - Verified & Approved)**

---

## 1. 🏛️ 설계 거버넌스 및 참여 AI 서브 에이전트

* **총괄 기획 & UI/UX 아키텍트**: **Claude Opus 4.6** (`dashboard-designer-opus`)
* **데이터 & 백엔드 엔진 아키텍트**: **Gemini 3.1 Pro** (`dashboard-engine-gemini`)
* **코드 품질 & 프론트엔드 심판**: **Claude Sonnet 4.6** (`dashboard-reviewer-sonnet`) - **[APPROVED]**
* **데이터 동시성 & 시스템 무결성 심판**: **Gemini 3.7 Thinking** (`dashboard-reviewer-gemini37`) - **[APPROVED]**
* **최상위 오케스트레이터**: **테르키르도 (Terukirdo Protocol v5.4)**

---

## 2. 🎨 UI/UX & Blazor 컴포넌트 아키텍처 (`Claude4Net.Dashboard.Client`)

### 2.1 디자인 철학: 리니어/커서 스타일 매트 옵시디언 (Matte Obsidian)
* **배경 베이스**: `#0b0d13` (Matte Charcoal Obsidian)
* **카드/패널 표면**: `#12151c` (Elevated Surface) + 헤어라인 경계선 (`1px solid rgba(255, 255, 255, 0.08)`)
* **절제된 기능적 액센트**:
  * Primary Accent: `#58a6ff` (Muted Tech Blue - 활성 텔레메트리, 핵심 인디케이터)
  * Success Accent: `#3fb950` (Precision Emerald - 정상 운영, 긍정적 지표)
  * Warning Accent: `#d29922` (Warm Amber - 경고, 리밋 근접)
  * Danger Accent: `#f85149` (Critical Rose - 승인 대기 뱃지, 에러)
  * Heatmap Tiers: `#161b22` (0) → `#0e4429` (L1) → `#006d32` (L2) → `#26a641` (L3) → `#39d353` (L4)

### 2.2 좌측 56px 슬림 액티비티 독 (`ActivityDock.razor`)
* **목적**: 화면 공간을 극대화하면서 마우스 호버 시 툴팁 및 원클릭 라우팅을 제공하는 초슬림 내비게이션 바.
* **주요 구성**:
  * 최상단: Claude4Net 미니멀 SVG 로고 마크
  * 🎛️ **Dashboard** (홈 관제 및 KPI)
  * 📅 **Calendar** (토큰/비용 캘린더 및 히트맵)
  * 🤖 **Agents** (멀티 에이전트 세션 및 타임라인)
  * 🛡️ **Approvals** (보안 결재 큐 - **실시간 `[3]` 위험 작업 대기 뱃지 카운트**)
  * 🔄 **Replay** (과거 세션 궤적 및 롤백 브라우저)
  * 💡 **Skills** (15대 전문 엔지니어링 스킬 카탈로그)
  * ⚙️ **Settings** (프로바이더 및 시스템 설정)
  * 최하단: **테르키르도 아바타 & 모드 퀵 전환 인디케이터** (클릭 시 팝오버로 Companion/Secretary/Orchestrator/Controller 즉시 전환)

### 2.3 중앙 📅 토큰 & 비용 액티비티 캘린더 (`CalendarView.razor`)
* **GitHub 스타일 연속 잔디 히트맵**:
  * 일별 총 토큰 소모량을 가로 스크롤 가능한 정밀 타일 매트릭스로 시각화
* **우측 시간대별(Hourly) 및 프로젝트 점유율 드릴다운 서랍 (Right Pane)**:
  * 특정 날짜 클릭 시 00:00 ~ 23:59 시간대별 토큰/비용 꺾은선 차트 및 프로젝트/에이전트별 점유율 즉시 분석 출력

### 2.4 하단 ⏱️ 분산 추적 워터폴 (`TraceWaterfall.razor`)
* **목적**: Datadog/Jaeger 스타일의 분산 실행 추적 타임라인으로 멀티 에이전트 레이턴시 및 병목 병렬 시각화.
* **추적 단계**:
  `API Gateway (20.8ms) → Orchestrator (28.6ms) → LLM Call (32.1ms) → Vector DB Embedding (12.3ms) → Response (17.1ms)`

### 2.5 하단 💻 IDE 스타일 사고 스트림 콘솔 (`ThoughtStream.razor`)
* 번쩍이는 효과 없이 실제 VS Code 터미널처럼 깔끔하고 가독성 높은 타임스탬프 CQRS 이벤트 로그 스트리밍 (10~15 FPS Throttling 적용).

---

## 3. 🛡️ 마스터 보안 승인 큐 (`ApprovalsQueue.razor` / `/approvals`)

* **목적**: `AdaptiveLoopTier.Tier3_HighRisk_Release` 및 `PrimeDirectiveCheckResult.RequiresApproval` 작업에 대한 주인님의 최종 승인/반려 인터페이스.
* **주요 기능**:
  * Git Diff 미리보기 및 변경 영향 범위(Impact Scope) 요약
  * **[👑 주인님 승인 (Approve)]** / **[❌ 반려 (Reject)]** 원클릭 액션 버튼

---

## 4. ⚡ TeruTeruPandas 텔레메트리 백엔드 데이터 엔진 (`Claude4Net.Runtime`)

### 4.1 `DataUniverse` 컬럼형 스토리지 스키마 (C# 정합성 검증 완료)
```csharp
public class TokenTelemetryTable
{
    public PrimitiveColumn<long> TimestampTicks { get; set; } = new("TimestampTicks");
    public StringColumn SessionId { get; set; } = new("SessionId");
    public StringColumn ProjectName { get; set; } = new("ProjectName");
    public StringColumn Provider { get; set; } = new("Provider");
    public StringColumn Model { get; set; } = new("Model");
    public PrimitiveColumn<int> PromptTokens { get; set; } = new("PromptTokens");
    public PrimitiveColumn<int> CompTokens { get; set; } = new("CompTokens");
    public PrimitiveColumn<int> TotalTokens { get; set; } = new("TotalTokens");
    public PrimitiveColumn<double> CostUsd { get; set; } = new("CostUsd");
    public PrimitiveColumn<double> LatencyMs { get; set; } = new("LatencyMs");
}

public class RequestTraceSpanTable
{
    public StringColumn SpanId { get; set; } = new("SpanId");
    public StringColumn TraceId { get; set; } = new("TraceId");
    public StringColumn ParentSpanId { get; set; } = new("ParentSpanId");
    public StringColumn ComponentName { get; set; } = new("ComponentName");
    public PrimitiveColumn<long> StartTimeTicks { get; set; } = new("StartTimeTicks");
    public PrimitiveColumn<double> DurationMs { get; set; } = new("DurationMs");
    public StringColumn Status { get; set; } = new("Status");
}
```

### 4.2 타임존 보정 및 시간대별/프로젝트별 SQL 쿼리 (`DataUniverseSql`)
```sql
-- 일별 토큰 히트맵 쿼리
SELECT 
    DATE(TimestampTicks + @TimezoneOffsetTicks) AS UsageDate,
    SUM(TotalTokens) AS DailyTotalTokens,
    SUM(CostUsd) AS DailyTotalCostUsd
FROM TokenTelemetry
GROUP BY DATE(TimestampTicks + @TimezoneOffsetTicks)
ORDER BY UsageDate DESC;

-- 시간대별 드릴다운 쿼리
SELECT 
    strftime('%H', datetime((TimestampTicks + @TimezoneOffsetTicks) / 10000000 - 62135596800, 'unixepoch')) AS HourSlot,
    SUM(TotalTokens) AS HourlyTokens,
    SUM(CostUsd) AS HourlyCost
FROM TokenTelemetry
WHERE DATE(TimestampTicks + @TimezoneOffsetTicks) = @SelectedDate
GROUP BY HourSlot
ORDER BY HourSlot ASC;
```

### 4.3 2026 플래그십 모델 단가표 (`PricingEngine`)
* Gemini 3.7 Flash/Pro, Claude 3.7 Sonnet/Opus, DeepSeek-V3/R1 정확한 토큰당 비용 계산.

### 4.4 SignalR 실시간 브로드캐스터 (`ControlPlaneHub`)
* **Tick Channel (5초)**: Throughput, Latency, `ApprovalsBadgeCount` 실시간 푸시.
* **Delta Log Channel**: 분산 추적 스팬 및 사고 스트림 비동기 스트리밍.
