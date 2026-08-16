# Documentation and Memory Policies

This document outlines the rules for modifying project documents, memory files, and automated documentation.

## Documentation Ownership

1. **SSOT and Design files (`product/`, `design/`, `architecture/`)**
   - Modified only with explicit user permission or direct request.
   - Plans must be proposed as candidates before implementation.
2. **Handoff & Progress ledger (`docs/handoff/`)**
   - Contains the canonical plan and progress. Can be updated dynamically as milestones are completed.
3. **Task logs & Verification evidence (`.agents/state/`)**
   - Automatically updated by hooks and tools on each run.

## Memory Preservation

- **Opt-In Only**: Session files like `MEMORY.md`, `docs/Terukirdo_memory.txt`, and `docs/Terukirdo_Trajectory.txt` are only updated if the user has opted-in.
- **Content Limitations**: Never record secrets, passwords, or personal identifying information (PII) in memory or trajectory logs.
- **Proposal vs Decision**: Distinguish clearly between proposed actions (prefix: `제안:`) and finalized user decisions (prefix: `확정 결정:`).

## Cluedoc Automated Documentation

- Default configuration is `auto_sync: false`.
- Automated generation of cluedoc files must only occur when explicitly requested or enabled by project policies.
