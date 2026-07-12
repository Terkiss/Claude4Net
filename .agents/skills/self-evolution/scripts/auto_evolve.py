#!/usr/bin/env python3
"""Self-Evolution safe wrapper.

This script orchestrates SkillOpt-Sleep to propose skill improvements.
It does NOT auto-commit or auto-apply changes. All proposed changes are
staged as a diff report for human review and explicit approval.

GUARDRAILS:
  - Never auto-commits skill changes. Only produces a diff report.
  - Never runs git reset --hard or git clean autonomously.
  - Only modifies files inside .agents/skills/ (Immutable Core rule).
  - Requires the user's explicit command to execute.
"""

import os
import subprocess
import sys
import json
from datetime import datetime

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
SKILLS_DIR = os.path.join(BASE_DIR, '.agents', 'skills')
HOOKS_DIR = os.path.join(BASE_DIR, '.agents', 'hooks')
REPORT_DIR = os.path.join(os.path.dirname(__file__), 'evolution_reports')


def run_cmd(cmd, cwd=None, check=False):
    """Run a command and return the result. Never raises on failure."""
    print(f"  > {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=cwd, check=check, capture_output=True, text=True)


def auto_evolve():
    """Run the self-evolution pipeline and produce a review-ready diff report."""
    print("=" * 60)
    print("  Self-Evolution Cycle (SkillOpt-Sleep)")
    print("  Mode: PROPOSE ONLY — changes require human approval")
    print("=" * 60)

    # 1. Verify clean working tree (refuse to run on dirty state)
    status = run_cmd(["git", "status", "--porcelain", ".agents/skills/"], cwd=BASE_DIR)
    if status.stdout.strip():
        print("\n[ABORT] Working tree has uncommitted changes in .agents/skills/.")
        print("Please commit or stash your changes first.")
        print("Uncommitted files:")
        for line in status.stdout.strip().split("\n"):
            print(f"  {line}")
        return 1

    # 2. Record current commit hash (safe point)
    safe_point = run_cmd(["git", "rev-parse", "HEAD"], cwd=BASE_DIR).stdout.strip()
    print(f"\nSafe point: {safe_point[:8]}")

    # 3. Trigger SkillOpt-Sleep Pipeline
    venv_python = os.path.join(os.path.dirname(__file__), 'skillopt-engine', 'venv', 'Scripts', 'python.exe')
    if not os.path.exists(venv_python):
        # Try Unix path as fallback
        venv_python = os.path.join(os.path.dirname(__file__), 'skillopt-engine', 'venv', 'bin', 'python')
    
    if not os.path.exists(venv_python):
        print(f"\n[SKIP] SkillOpt venv not found. Set up first:")
        print(f"  cd .agents/skills/self-evolution/scripts/skillopt-engine")
        print(f"  python -m venv venv && pip install -e .")
        return 1

    api_key_set = "AZURE_OPENAI_API_KEY" in os.environ or "OPENAI_API_KEY" in os.environ
    backend_args = []
    if not api_key_set:
        try:
            agy_check = run_cmd(["agy", "--version"], cwd=BASE_DIR)
            if agy_check.returncode == 0:
                print("\n[INFO] No API key found. Using local 'agy' backend.")
                backend_args = ["--backend", "agy"]
            else:
                print("\n[ERROR] No API key found and 'agy' CLI is not available.")
                return 1
        except Exception:
            print("\n[ERROR] No API key found and 'agy' CLI is not available.")
            return 1

    print("\nRunning SkillOpt-Sleep pipeline...")
    try:
        engine_dir = os.path.join(os.path.dirname(__file__), 'skillopt-engine')
        run_cmd(
            [venv_python, "-m", "skillopt_sleep", "--source", "docs", "--mine", "--rollout"] + backend_args,
            cwd=engine_dir
        )
    except Exception as e:
        print(f"\n[ERROR] SkillOpt engine failed: {e}")
        return 1

    # 4. Check what changed
    diff_result = run_cmd(["git", "diff", "--stat", ".agents/skills/"], cwd=BASE_DIR)
    diff_detail = run_cmd(["git", "diff", ".agents/skills/"], cwd=BASE_DIR)

    if not diff_result.stdout.strip():
        print("\n[RESULT] No skill changes proposed. Skills are already optimal.")
        return 0

    # 5. Run Quality Gates on proposed changes (advisory only)
    print("\nRunning Quality Gates on proposed changes...")
    qg_result = run_cmd([sys.executable, os.path.join(HOOKS_DIR, "stop_quality_gate.py")], cwd=BASE_DIR)
    qg_passed = qg_result.returncode == 0

    # 6. Generate diff report for human review (NEVER auto-commit)
    os.makedirs(REPORT_DIR, exist_ok=True)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    report_path = os.path.join(REPORT_DIR, f"evolution_{timestamp}.md")

    with open(report_path, 'w', encoding='utf-8') as f:
        f.write(f"# Self-Evolution Report — {timestamp}\n\n")
        f.write(f"**Safe point:** `{safe_point[:8]}`\n")
        f.write(f"**Quality Gate:** {'✅ PASSED' if qg_passed else '❌ FAILED'}\n\n")
        f.write("## Changed Files\n\n```\n")
        f.write(diff_result.stdout)
        f.write("```\n\n")
        f.write("## Diff Detail\n\n```diff\n")
        f.write(diff_detail.stdout)
        f.write("```\n\n")
        f.write("## Next Steps\n\n")
        f.write("Review the changes above. To apply:\n")
        f.write("```powershell\n")
        f.write("git add .agents/skills/\n")
        f.write('git commit -m "feat: apply self-evolution skill improvements"\n')
        f.write("```\n\n")
        f.write("To discard:\n")
        f.write("```powershell\n")
        f.write("git checkout -- .agents/skills/\n")
        f.write("```\n")

    # 7. Restore working tree to clean state (discard engine's proposed changes)
    # The proposed changes stay ONLY in the report file, not in the working tree
    run_cmd(["git", "checkout", "--", ".agents/skills/"], cwd=BASE_DIR)

    print(f"\n{'=' * 60}")
    print(f"  EVOLUTION COMPLETE — Review required")
    print(f"{'=' * 60}")
    print(f"\n  Quality Gate: {'✅ PASSED' if qg_passed else '❌ FAILED'}")
    print(f"  Report saved: {report_path}")
    print(f"\n  Changes are NOT applied.")
    print(f"  Please review the report and apply manually if approved.")
    return 0


if __name__ == "__main__":
    raise SystemExit(auto_evolve())
