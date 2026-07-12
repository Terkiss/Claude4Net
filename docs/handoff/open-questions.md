# Open Questions

답이 필요한 질문을 기록한다. 추측으로 결정하지 않는다.

## Active Questions

### Question: Short title

- Blocking: yes | no
- Needed from:
- Target doc after answer:
- Insert section after answer:
- Reason:

#### Options

- Option A:
- Option B:
- Option C:

#### Notes

- 

## Rules

- Blocking question은 답변 전 구현, release, 데이터 변경, 보안/권한 결정을 진행하면 위험한 질문이다.
- Blocking question은 구현이나 release 전에 먼저 대화에서 사용자에게 직접 묻는다.
- 질문은 갱신 필요성을 보고하고 사용자 요청 또는 승인 후에만 기록한다.
- 답변된 질문은 사용자 요청 또는 승인 후 `docs/handoff/decisions.md` 또는 관련 domain docs 후보로 옮긴다.
- 추측성 아이디어는 action을 막는 불확실성이 아니면 기록하지 않는다.

## Resolution Flow

1. Try to answer from existing code, docs, user messages, or confirmed external contract.
2. If unresolved and blocking, ask the user before implementation or release.
3. If answered, report the related doc update need and update only after user request or approval.
4. If deferred, provide options, record only after user request or approval, and reduce the current work scope.
5. If the user explicitly asks for temporary progress, keep it reversible and include residual risk in the final response.
