#!/usr/bin/env python3
import sys
import os
import json
import subprocess
from pathlib import Path
from common import load_input, respond_json, log_stderr, find_repo_root

STATE_FILE = Path(".agents/state/stop_gate_state.json")
MAX_ATTEMPTS = 2

def get_git_status_porcelain(root: Path) -> list:
    try:
        out = subprocess.check_output(
            ["git", "status", "--porcelain"],
            cwd=root,
            stderr=subprocess.DEVNULL,
            text=True,
        )
        return [line.strip() for line in out.splitlines() if line.strip()]
    except Exception:
        return []

def run_git_diff_check(root: Path) -> bool:
    try:
        res1 = subprocess.run(["git", "diff", "--check"], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        res2 = subprocess.run(["git", "diff", "--cached", "--check"], cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if res1.returncode != 0 or res2.returncode != 0:
            log_stderr(f"Git diff check failed: {res1.stderr.decode()} {res2.stderr.decode()}")
            return False
        return True
    except Exception as e:
        log_stderr(f"Failed to run git diff --check: {e}")
        return True

def run_harness_validator(root: Path) -> bool:
    try:
        res = subprocess.run(
            [sys.executable, "scripts/harness/validate_harness.py", "--strict"],
            cwd=root,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True
        )
        if res.returncode != 0:
            log_stderr(f"validate_harness.py failed:\n{res.stderr}")
            return False
        return True
    except Exception as e:
        log_stderr(f"Failed to run validate_harness.py: {e}")
        return False

def load_stop_state() -> dict:
    if STATE_FILE.is_file():
        try:
            return json.loads(STATE_FILE.read_text(encoding="utf-8"))
        except Exception:
            pass
    return {"attempt": 0}

def save_stop_state(state: dict):
    try:
        STATE_FILE.parent.mkdir(parents=True, exist_ok=True)
        STATE_FILE.write_text(json.dumps(state, indent=2), encoding="utf-8")
    except Exception as e:
        log_stderr(f"Failed to save stop gate state: {e}")

def main() -> int:
    try:
        payload = load_input()
    except Exception as e:
        log_stderr(f"Stop Hook input parse exception: {e}")
        respond_json({"status": "failed", "reason": f"Input parse error: {e}"})
        return 0

    root = find_repo_root()

    # 1. Check if workspace has meaningful modifications
    status_lines = get_git_status_porcelain(root)
    code_modified = False
    meaningful_work = False
    memory_synced = False

    for line in status_lines:
        parts = line.split(None, 1)
        if len(parts) < 2:
            continue
        path_str = parts[1].strip()
        p_lower = path_str.replace('\\', '/').lower()

        if p_lower == ".agents/state/stop_gate_state.json":
            continue

        if p_lower in ["memory.md", "docs/terukirdo_trajectory.txt"]:
            memory_synced = True
            continue

        meaningful_work = True

        # Harness files prefixes
        is_harness = any(p_lower.startswith(pref) for pref in [
            ".agents/", "scripts/", "tests/", "docs/", "agents/",
            "terukirdo_protocol_", "agents.md", "readme.md", "테르키르도.zip", ".gitignore"
        ])
        if not is_harness and not path_str.endswith(".md"):
            code_modified = True

    if not meaningful_work and not memory_synced:
        log_stderr("No files modified. Tier 0/1 chat/query bypass active.")
        respond_json({"status": "passed", "reason": "No modifications detected."})
        return 0

    # 2. Track attempts to prevent infinite loop
    state = load_stop_state()
    state["attempt"] += 1
    save_stop_state(state)

    log_stderr(f"Evaluating Stop Gate (Attempt {state['attempt']}/{MAX_ATTEMPTS})")

    failures = []

    # Check 1: validate_harness.py
    if not run_harness_validator(root):
        failures.append("Harness structural validation failed (validate_harness.py)")

    # Check 2: Git diff --check
    if not run_git_diff_check(root):
        failures.append("Git diff whitespace or conflict checks failed (git diff --check)")

    # Check 3: Deterministic Memory Sync
    if meaningful_work and not memory_synced:
        failures.append("Turn-End Memory Sync required: You have modified files but didn't update MEMORY.md or docs/Terukirdo_Trajectory.txt.")

    if failures:
        reason = "; ".join(failures)
        if state["attempt"] >= MAX_ATTEMPTS:
            log_stderr(f"Max attempts exceeded. Stop Hook BLOCKED: {reason}")
            save_stop_state({"attempt": 0})
            respond_json({"status": "blocked", "reason": f"Gate blocked after repeated failure: {reason}"})
        else:
            log_stderr(f"Stop Hook CONTINUE requested: {reason}")
            respond_json({"status": "continue", "reason": f"Quality gate checks failed. Please resolve: {reason}"})
    else:
        log_stderr("Stop Hook PASSED.")
        save_stop_state({"attempt": 0})
        respond_json({"status": "passed", "reason": "All checks passed successfully."})

    return 0

if __name__ == "__main__":
    main()
