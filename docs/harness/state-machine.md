# Harness State Machine

This document describes the lifecycle states and transition rules of the Terukirdo v5.3 task execution engine.

## Lifecycle States

- `REQUEST RECEIVED`: User request has been loaded.
- `INTENT/RISK CLASSIFIED`: Intent, risk level, and adaptive loop tier have been determined.
- `PLAN CANDIDATE`: Milestone and implementation plans have been generated.
- `READY FOR EXECUTION`: Execution Card and scopes are finalized.
- `IMPLEMENTED, UNVERIFIED`: Changes written but not yet tested.
- `REVIEW IN PROGRESS`: First Reviewer and Tech Expert are evaluating changes.
- `REWORK REQUIRED`: P1 or P2 findings are open; task returned to Worker.
- `VERIFIED FOR FINAL CONTROL`: UFC has completed verification; ready for Final Approach Control.
- `APPROVED FOR COMMIT ONLY`: Final Approach Control has verified the raw index and approved local commit.
- `COMMITTED LOCALLY`: Local commit has been made; HEAD commit hash recorded.
- `PUSH APPROVAL REQUIRED`: Remote push requires explicit user verification.
- `REJECTED`: Completion denied due to lack of evidence or scope violations.
- `BLOCKED`: Blocked by Stop Hook retry limits or critical system blockers.

## Transition Rules

1. **Rework Transition**: If any `P1` or `P2` finding is opened during review, state changes to `REWORK REQUIRED`.
2. **Commit Gate**: Transition to `COMMITTED LOCALLY` requires an active `APPROVED FOR COMMIT ONLY` verdict from Final Approach Control.
3. **No Push in Loop**: No remote push is executed inside the Ralph Loop. It must transition to `PUSH APPROVAL REQUIRED` for user-directed execution.
