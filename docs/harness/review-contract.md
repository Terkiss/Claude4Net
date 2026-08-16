# Review Contract

This document defines the interface and data contracts for review processes.

## Review Report Schema

All reviewers must generate reports conforming to the JSON Schema at [.agents/schemas/review-report.schema.json](file:///D:/Project/codex-flutter/newAntigravity/.agents/schemas/review-report.schema.json).

### First Reviewer Verdicts

- `PASS`: Code changes meet all requirements, with no issues.
- `PASS WITH P3`: Code meets requirements with only minor stylistic suggestions (P3).
- `REWORK REQUIRED`: Critical (P1) or major (P2) issues found. Rework is required.
- `UNABLE TO VERIFY`: Incomplete files or missing logs prevent review.

### Tech Expert (Architectural Judge) Verdicts

- `ARCHITECTURALLY ACCEPTABLE`: Technical choices, module boundaries, and security policies are sound.
- `REWORK REQUIRED`: Architectural or security violations (P1/P2) detected.
- `EXPLORATION ONLY`: Approved only as a prototype; not for production staging.
- `UNABLE TO VERIFY`: Lack of documentation or remote access blocks validation.

## Review Rules

1. **Strict Read-Only**: Reviewers must not modify implementation code or git index states.
2. **Finding Severity**:
   - `P1` (Security/Data loss): Must be fixed before staging.
   - `P2` (Architecture/Bugs): Must be fixed before staging.
   - `P3` (Stylistic/Docs): Can be deferred and documented as residual risks.
