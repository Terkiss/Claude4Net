# Approval Terminology

This document defines standard terms and validation requirements for final verdicts in Terukirdo v5.3.

## Standard Verdict Terms

To prevent ambiguity and security failures:

- **Forbidden Term**: Single `APPROVED` is strictly forbidden in reports, protocols, or logs.
- **Local Commit Verdict**: `APPROVED FOR COMMIT ONLY` is the only valid status indicating local staged changes are correct and ready for commit.
- **Final Staging Verdict**: `VERIFIED FOR FINAL CONTROL` indicates UFC has verified the changes, and they are ready for FAC review.

## Authority Boundaries

1. **Local Commit**: FAC can grant `APPROVED FOR COMMIT ONLY`. Commits must record the local `HEAD` hash.
2. **Remote Push**: Requires separate, explicit user approval.
3. **Deployment / Release**: Requires separate, explicit user approval.

## Findings Severity Definitions

- `P1 (Critical / Security / Data loss)`: Must be fixed. Blocks all staging and commits.
- `P2 (Major / Architecture / Bugs)`: Must be fixed before staging. Blocks all staging and commits.
- `P3 (Minor / Styles / Documentation)`: Optional. Can be deferred.
