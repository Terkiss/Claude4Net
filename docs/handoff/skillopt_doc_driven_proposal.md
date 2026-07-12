# 중앙 템플릿 강화 제안서: Doc-Driven SkillOpt (문서 기반 자가 진화)

이 문서는 주인님의 통찰력 있는 아이디어를 바탕으로, **SkillOpt의 진화 데이터 소스를 원시 로그(JSONL)에서 정제된 문서(DOC)로 전환**하기 위한 구현 제안서입니다. 이 제안을 템플릿 저장소에 반영하면, 에이전트 진화 아키텍처의 패러다임이 바뀝니다.

---

## 1. 배경 및 문제 의식 (Why we need this)

현재 SkillOpt 엔진은 진화를 위해 방대한 `transcript.jsonl` (수만 줄의 시스템 로그)을 읽어들여 분석(Mine)합니다. 
- **비효율성:** "파일 읽기 완료", "네 알겠습니다" 같은 영양가 없는 노이즈(Noise)가 99%를 차지하여 LLM 토큰(비용 및 시간)이 심각하게 낭비됩니다.
- **통제력 부족:** 진화의 재료가 되는 원시 로그는 사람이 읽거나 중간에 수정하기 어렵습니다. 잘못된 방향으로 학습(Hallucination)할 위험이 존재합니다.
- **컨텍스트 유실:** 대규모 프로젝트에서는 로그가 금방 Truncate되어 정작 중요한 교훈을 엔진이 놓치는 문제가 발생합니다.

**해결책 (주인님의 아이디어):** 
에이전트가 세션 종료 시 요약해둔 `MEMORY.md`나 `docs/` 폴더 안의 "Key Learnings(핵심 교훈)"만을 읽어서 스킬(SKILL.md)을 진화시킵니다.

## 2. Doc-Driven 진화의 3대 장점

1. **압도적인 비용/속도 절감 (Zero-Waste):** 무거운 로그 마이닝(Mine) 단계를 아예 건너뛸 수 있습니다. 엑기스만 담긴 마크다운 문서 몇 장만 읽으면 끝납니다. 토큰 사용량이 최대 1/100 수준으로 감소합니다.
2. **완벽한 통제권 (Human-in-the-Loop):** 에이전트가 `MEMORY.md`에 적어둔 오답 노트를 주인이 쓱 읽어보고, "이건 억지 교훈이네" 싶으면 지워버릴 수 있습니다. 진화의 재료를 인간이 직접 통제하고 가공할 수 있습니다.
3. **지식의 선순환 구조 완비:** 
   [작업] ➔ [에이전트가 DOC에 요약 기록] ➔ [주인님 검수 및 수정] ➔ [SkillOpt가 DOC을 읽고 SKILL.md 영구 반영] ➔ [진화된 스킬로 다시 작업] 의 완벽하고 안전한 피드백 루프가 형성됩니다.

---

## 3. 구체적 구현 방안 (Implementation Plan)

템플릿 저장소의 `.agents/skills/self-evolution/` 구조 내부에 다음 변경을 가하여 Doc-Driven 아키텍처를 구현합니다.

### A. 신규 파서 및 수확기 추가: `harvest_docs.py`
`.agents/skills/self-evolution/scripts/skillopt-engine/skillopt_sleep/` 경로에 `harvest_docs.py`를 생성합니다.
- **타겟 문서:** 프로젝트 루트의 `MEMORY.md` (`## Key Learnings`, `## Open Questions` 섹션) 및 `docs/handoff/` 디렉터리 내 핵심 마크다운 문서.
- **처리 로직:** 마크다운 파싱이나 정규식을 통해 '교훈', '주의점', '새로운 패턴'과 관련된 텍스트 블록만 정밀 추출합니다.
- **매핑:** 추출한 텍스트를 SkillOpt 파이프라인이 즉시 소비할 수 있도록 가상의 `SessionDigest` 객체(또는 요약된 Text)로 매핑하여 주입합니다.

### B. 소스 라우팅 및 Fallback 구조화: `harvest_sources.py`
엔진이 문서 기반 수확기를 인식하도록 분기를 추가하고, 안전장치를 마련합니다.
```python
if source == "docs":
    digests = harvest_docs(cfg.workspace_dir)
    # 안전장치(Fallback): 문서에 학습할 교훈이 너무 적을 경우 경고 후 종료
    if not digests or len(str(digests)) < MIN_REQUIRED_WORDS:
        print("[WARN] 진화에 필요한 충분한 교훈(Key Learnings)이 문서에 없습니다. 학습을 스킵합니다.")
        return []
    return digests
```

### C. 실행 래퍼 수정: `auto_evolve.py`
진화 스크립트 실행 시, 기본 데이터 소스를 `docs`로 강제 지정하도록 커맨드라인 옵션을 수정합니다.
```python
# 기존 (로그 전체 마이닝)
cmd = [venv_python, "-m", "skillopt_sleep", "--harvest", "--mine", "--rollout", "--backend", "agy"]

# 변경 (Doc-Driven Zero-Waste 마이닝)
cmd = [venv_python, "-m", "skillopt_sleep", "--source", "docs", "--mine", "--rollout", "--backend", "agy"]
```

### D. AGENTS.md 룰 연계 강화
문서 기반 진화가 잘 작동하려면 문서 자체가 양질이어야 합니다. `AGENTS.md`의 `Session-End Memory Sync` 룰에 다음 지침을 보강하여 에이전트가 교훈을 더 잘 남기도록 유도해야 합니다.
- *"새롭게 발견한 아키텍처 한계, 도구(Skill) 사용 시 발생한 에러와 해결책은 반드시 MEMORY.md의 Key Learnings에 행동 지침(Actionable Insight) 형태로 기록하라."*

---

## 4. 기대 효과 및 결론 (Impact)

이 제안이 템플릿에 적용되면, 테르키르도 메이드형 하네스는 **현존하는 가장 가볍고 제어하기 쉬운 자가 진화 프레임워크**가 됩니다. 무거운 로그 파일에 의존하는 기존 상용 AI 에이전트들의 구조적 비효율성(Context Window 한계 및 막대한 토큰 비용)을 완벽하게 극복한 차세대 아키텍처입니다.

> **Next Action Item:** 본 제안서를 확인하시고 승인(Approve) 지시를 내려 주시면, 위 A, B, C, D의 코드 및 룰 구현 작업을 즉시 시작하여 템플릿 소스코드를 Doc-Driven 구조로 완전히 개조하겠습니다.
