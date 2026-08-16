# Evidence Policy

This document defines the rules for verification evidence gathering and validation in Terukirdo v5.3.

## Evidence Bundle

All verification results must be aggregated into an **Evidence Bundle** matching [.agents/schemas/evidence-bundle.schema.json](file:///D:/Project/codex-flutter/newAntigravity/.agents/schemas/evidence-bundle.schema.json).

### Status Values for Verification Commands

All executed test or verification commands must return one of the following statuses:

- `PASS`: The command executed successfully and all assertions passed.
- `FAIL`: The command failed (exit code non-zero) or assertion failures were detected.
- `SKIPPED_NO_COMMAND`: No verification command was configured or found for the target action.
- `SKIPPED_NOT_APPLICABLE`: The verification was not applicable in this context.
- `BLOCKED_ENVIRONMENT`: Verification could not run due to local environment limits or missing tools.
- `NOT_RUN`: The command was configured but has not yet been executed.

## Verification Rules

1. **No False Pass**: A task must never report `PASS` unless the command was actually executed and succeeded. If no command ran, it must be reported as `SKIPPED_NO_COMMAND` or `NOT_RUN`.
2. **Insufficient Evidence**: If any required verification command is `NOT_RUN`, the Universal Final Controller or Final Approach Control will reject the completion with `REJECTED — EVIDENCE INSUFFICIENT`.
3. **P1/P2 Blockers**: Any open findings with severity `P1` or `P2` will automatically block completion.
